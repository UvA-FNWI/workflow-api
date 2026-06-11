using UvA.Workflow.Api.Screens.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Screens;

public class ScreenDataService(
    ModelService modelService,
    InstanceService instanceService,
    IWorkflowInstanceRepository repository,
    InstanceAuthorizationFilterService instanceAuthorizationFilterService)
{
    /// <summary>
    /// Gets screen data for the given screen. When the screen defines a grouping configuration,
    /// the rows are partitioned into groups by their current workflow step (and the flat row list
    /// is left empty); otherwise a flat row list is returned.
    /// </summary>
    public async Task<ScreenDataDto> GetScreenData(string screenName, string workflowDefinition, CancellationToken ct)
    {
        // Get the screen definition
        var screen = GetScreen(screenName, workflowDefinition);
        if (screen == null)
            throw new ArgumentException($"Screen '{screenName}' not found for entity type '{workflowDefinition}'");

        // Build projection based on screen columns
        var contexts = await LoadData(screen, workflowDefinition, ct);

        // Process the data and apply templates/expressions
        var columns = screen.Columns.Select(ScreenColumnDto.Create).ToArray();

        // When the screen is grouped, return groups instead of a flat row list
        if (screen.Grouping != null)
        {
            var groups = BuildGroups(contexts, screen, columns);
            return ScreenDataDto.Create(screen, columns, [], groups);
        }

        var rows = ProcessRows(contexts, screen, columns);
        return ScreenDataDto.Create(screen, columns, rows);
    }

    private Screen? GetScreen(string screenName, string workflowDefinition)
    {
        if (!modelService.WorkflowDefinitions.TryGetValue(workflowDefinition, out var entity))
            return null;

        return entity.Screens.GetOrDefault(screenName);
    }

    private Dictionary<string, string> BuildProjection(Column[] columns, string workflowDefinition)
    {
        if (!modelService.WorkflowDefinitions.TryGetValue(workflowDefinition, out var entity))
            throw new ArgumentException($"Entity type '{workflowDefinition}' not found");

        var projection = new Dictionary<string, string>();

        foreach (var column in columns)
        {
            if (column.CurrentStep)
                projection["CurrentStep"] = "$CurrentStep";
            foreach (var prop in column.Properties)
                AddLookupToProjection(projection, prop, entity);
        }

        return projection;
    }

    private void AddLookupToProjection(Dictionary<string, string> projection, Lookup lookup, WorkflowDefinition entity)
    {
        switch (lookup)
        {
            case PropertyLookup propertyLookup:
                var propertyName = propertyLookup.Property.Split('.')[0];
                var mongoPath = entity.GetKey(propertyName);
                projection.TryAdd(propertyName, mongoPath);
                break;
            case ComplexLookup complexLookup:
                // For complex lookups, we need to add properties from their arguments
                foreach (var arg in complexLookup.Arguments)
                {
                    foreach (var prop in arg.Properties)
                    {
                        AddLookupToProjection(projection, prop, entity);
                    }
                }

                break;
        }
    }

    private ScreenRowDto[] ProcessRows(
        ICollection<ObjectContext> contexts,
        Screen screen,
        ScreenColumnDto[] columns
    )
    {
        var rows = new List<ScreenRowDto>();

        foreach (var context in contexts)
        {
            var id = context.Id!;
            var processedValues = new Dictionary<int, object?>();

            // Process each column and use its ID as the key
            for (int i = 0; i < screen.Columns.Length; i++)
            {
                var column = screen.Columns[i];
                var columnId = columns[i].Id;
                var value = columns[i].IsCurrentStep
                    ? GetCurrentStepProgress(screen, column.GetValue(context) as string ?? "", context)
                    : column.GetValue(context);
                processedValues[columnId] = value;
            }

            rows.Add(ScreenRowDto.Create(id, processedValues));
        }

        return rows.ToArray();
    }

    private async Task<List<ObjectContext>> LoadData(Screen screen, string workflowDefinition, CancellationToken ct)
    {
        // Build projection based on screen columns, always including CurrentStep for grouping
        var projection = BuildProjection(screen.Columns, workflowDefinition);
        projection.TryAdd("CurrentStep", "$CurrentStep");
        projection.TryAdd("Events", "$Events");

        // Build authorization filter to restrict instances to those the user can view
        var authorizationFilter =
            await instanceAuthorizationFilterService.BuildAuthorizationFilter(workflowDefinition, ct);

        var rawData = await repository.GetAllByType(workflowDefinition, projection, authorizationFilter, ct);
        var contexts = rawData.Select(r => modelService.CreateContext(workflowDefinition, r)).ToList();

        // Add related properties as needed
        await instanceService.Enrich(modelService.WorkflowDefinitions[workflowDefinition],
            contexts, screen.Columns.SelectMany(c => c.Properties), ct, false);

        return contexts;
    }

    /// <summary>
    /// Partitions the loaded instances into the screen's configured groups, keyed by their current
    /// workflow step. All configured groups are always included (even when empty); instances whose
    /// step does not match any group are dropped.
    /// </summary>
    private ScreenGroupDto[] BuildGroups(
        ICollection<ObjectContext> contexts,
        Screen screen,
        ScreenColumnDto[] columns)
    {
        // Build step-to-group mapping from configuration
        var stepGroupMapping = BuildStepGroupMapping(screen.Grouping!);

        // Group raw rows by step
        var groupedContexts = new Dictionary<string, List<ObjectContext>>(StringComparer.OrdinalIgnoreCase);

        foreach (var context in contexts)
        {
            var stepValue = context.Get("CurrentStep")?.ToString() ?? "Draft";

            // Only include rows that match a configured group
            if (!stepGroupMapping.TryGetValue(stepValue, out var groupName))
                continue;

            if (!groupedContexts.TryGetValue(groupName, out var list))
            {
                list = [];
                groupedContexts[groupName] = list;
            }

            list.Add(context);
        }

        // Build the result with group metadata (always include all configured groups)
        return screen.Grouping!.Groups
            .Select(g => new ScreenGroupDto(
                g.Name,
                g.Title,
                ProcessRows(groupedContexts.TryGetValue(g.Name, out var ctx) ? ctx : [], screen, columns)))
            .ToArray();
    }

    private static Dictionary<string, string> BuildStepGroupMapping(ScreenGrouping grouping)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouping.Groups)
        {
            foreach (var step in group.Steps)
            {
                mapping[step] = group.Name;
            }
        }

        return mapping;
    }

    public record ProgressInformationDto(
        BilingualString Text,
        StatusColor? Color)
    {
        public static ProgressInformationDto Create(Step step, ObjectContext context)
        {
            var displayText = step.Progress?.ProgressTextTemplate?.Apply(context)
                              ?? step.DisplayTitle;
            return new ProgressInformationDto(displayText, step.Progress?.Color);
        }
    }

    private ProgressInformationDto GetCurrentStepProgress(Screen screen, string internalName, ObjectContext context)
    {
        if (string.IsNullOrEmpty(screen.WorkflowDefinition) ||
            !modelService.WorkflowDefinitions.TryGetValue(screen.WorkflowDefinition, out var workflowDef))
            return new ProgressInformationDto(new BilingualString(internalName, internalName), null);

        var currentStep = workflowDef.AllSteps.Find(s => s.Name == internalName);

        return currentStep == null
            ? new ProgressInformationDto(new BilingualString(internalName, internalName), null)
            : ProgressInformationDto.Create(currentStep, context);
    }
}