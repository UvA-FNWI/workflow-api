using UvA.Workflow.Api.Screens.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Screens;

public class ScreenDataService(
    ModelService modelService,
    InstanceService instanceService,
    IWorkflowInstanceRepository repository,
    InstanceAuthorizationFilterService instanceAuthorizationFilterService,
    RightsService rightsService)
{
    private static readonly string EmptyStepId = "null";

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
        var definition = modelService.WorkflowDefinitions[workflowDefinition];

        var canCreateInstance = await rightsService.CanAny(workflowDefinition, RoleAction.CreateInstance);

        // Build projection based on screen columns
        var contexts = await LoadData(screen, workflowDefinition, ct);

        // Process the data and apply templates/expressions
        var columns = screen.Columns.Select(ScreenColumnDto.Create).ToArray();

        // When the screen is grouped, return groups instead of a flat row list
        if (screen.Grouping != null)
        {
            var groups = BuildGroups(contexts, screen, columns);
            return ScreenDataDto.Create(screen, definition, columns, [], groups, canCreateInstance);
        }

        var rows = ProcessRows(contexts, screen, columns);
        return ScreenDataDto.Create(screen, definition, columns, rows, canCreateInstance: canCreateInstance);
    }

    private Screen? GetScreen(string screenName, string workflowDefinition)
    {
        if (!modelService.WorkflowDefinitions.TryGetValue(workflowDefinition, out var entity))
            return null;

        return entity.Screens.GetOrDefault(screenName);
    }

    private Dictionary<string, string> BuildProjection(
        Column[] columns,
        string workflowDefinition,
        IEnumerable<Lookup> progressLookups)
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

        foreach (var lookup in progressLookups)
            AddLookupToProjection(projection, lookup, entity);

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
        var definition = modelService.WorkflowDefinitions[workflowDefinition];
        var hasProgressColumn = screen.Columns.Any(column => column.CurrentStep);
        var progressLookups = hasProgressColumn
            ? definition.ProgressLookups.ToArray()
            : [];

        // Build projection based on screen columns, always including CurrentStep for grouping
        var projection = BuildProjection(screen.Columns, workflowDefinition, progressLookups);
        projection.TryAdd("CurrentStep", "$CurrentStep");
        projection.TryAdd("Events", "$Events");

        // Build authorization filter to restrict instances to those the user can view
        var authorizationFilter =
            await instanceAuthorizationFilterService.BuildAuthorizationFilter(workflowDefinition, ct);

        var rawData = await repository.GetAllByType(workflowDefinition, projection, authorizationFilter, ct);
        var contexts = rawData.Select(row => modelService.CreateContext(workflowDefinition, row)).ToList();

        // Add related properties as needed
        await instanceService.Enrich(definition,
            contexts, screen.Columns.SelectMany(c => c.Properties).Concat(progressLookups), ct, false);

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
            var stepValue = context.Get("CurrentStep")?.ToString() ?? EmptyStepId;

            // Only include rows that match a configured group
            var definition = modelService.WorkflowDefinitions.GetValueOrDefault(screen.WorkflowDefinition ?? "");
            string? groupName;
            while (!stepGroupMapping.TryGetValue(stepValue, out groupName))
            {
                stepValue = definition?.AllSteps.FirstOrDefault(s => s.Name == stepValue)?.ParentStep?.Name;
                if (stepValue == null)
                    break;
            }

            if (groupName == null)
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
                mapping[step ?? EmptyStepId] = group.Name;
            }
        }

        return mapping;
    }

    private ProgressInformationDto GetCurrentStepProgress(Screen screen, string internalName, ObjectContext context)
    {
        if (string.IsNullOrEmpty(screen.WorkflowDefinition) ||
            !modelService.WorkflowDefinitions.TryGetValue(screen.WorkflowDefinition, out var workflowDef))
            return new ProgressInformationDto(new BilingualString(internalName, internalName), null);

        return ProgressInformationDto.Resolve(workflowDef, internalName, context);
    }
}