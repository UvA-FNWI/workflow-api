using UvA.Workflow.Api.Screens.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Screens;

public class ScreenDataService(
    ModelService modelService,
    InstanceService instanceService,
    IWorkflowInstanceRepository repository)
{
    public async Task<ScreenDataDto> GetScreenData(string screenName, string workflowDefinition, CancellationToken ct)
    {
        // Get the screen definition
        var screen = GetScreen(screenName, workflowDefinition);
        if (screen == null)
            throw new ArgumentException($"Screen '{screenName}' not found for entity type '{workflowDefinition}'");

        // Build projection based on screen columns
        var projection = BuildProjection(screen.Columns, workflowDefinition);
        var rawData = await repository.GetAllByType(workflowDefinition, projection, ct);
        var contexts = rawData.Select(r => modelService.CreateContext(workflowDefinition, r)).ToList();

        // Add related properties as needed
        await instanceService.Enrich(modelService.WorkflowDefinitions[workflowDefinition],
            contexts, screen.Columns.SelectMany(c => c.Properties), ct);

        // Process the data and apply templates/expressions
        var columns = screen.Columns.Select(ScreenColumnDto.Create).ToArray();
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
                var value = column.GetValue(context);
                processedValues[columnId] = value;
            }

            rows.Add(ScreenRowDto.Create(id, processedValues));
        }

        return rows.ToArray();
    }

    /// <summary>
    /// Gets screen data grouped by workflow step using the grouping configuration from the screen definition.
    /// CurrentStep is automatically fetched for grouping purposes, regardless of whether it's defined in the columns.
    /// </summary>
    /// <param name="screenName">The name of the screen</param>
    /// <param name="workflowDefinition">The workflow definition</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Grouped screen data with bilingual titles for each group</returns>
    public async Task<GroupedScreenDataDto> GetGroupedScreenData(string screenName, string workflowDefinition,
        CancellationToken ct)
    {
        var screen = GetScreen(screenName, workflowDefinition);
        if (screen == null)
            throw new ArgumentException($"Screen '{screenName}' not found for entity type '{workflowDefinition}'");

        if (screen.Grouping == null)
            throw new ArgumentException($"Screen '{screenName}' does not have grouping configuration");

        // Build projection based on screen columns, always including CurrentStep for grouping
        var projection = BuildProjection(screen.Columns, workflowDefinition);
        projection.TryAdd("CurrentStep", "$CurrentStep");

        var rawData = await repository.GetAllByType(workflowDefinition, projection, ct);
        var contexts = rawData.Select(r => modelService.CreateContext(workflowDefinition, r)).ToList();

        // Build step-to-group mapping from configuration
        var stepGroupMapping = BuildStepGroupMapping(screen.Grouping);

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

        // Process columns
        var columns = screen.Columns.Select(ScreenColumnDto.Create).ToArray();

        // Build the result with group metadata (always include all configured groups)
        var groups = screen.Grouping.Groups
            .Select(g => new ScreenGroupDto(
                g.Name,
                g.Title,
                ProcessGroupRows(groupedContexts.TryGetValue(g.Name, out var ctx) ? ctx : [], screen, columns)))
            .ToArray();

        return new GroupedScreenDataDto(
            screen.Name,
            screen.WorkflowDefinition ?? "",
            columns,
            groups);
    }

    private ScreenRowDto[] ProcessGroupRows(
        ICollection<ObjectContext> contexts,
        Screen screen,
        ScreenColumnDto[] columns)
    {
        var rows = new List<ScreenRowDto>();

        foreach (var context in contexts)
        {
            var id = context.Id ?? "Unknown";
            var processedValues = new Dictionary<int, object?>();

            for (int i = 0; i < screen.Columns.Length; i++)
            {
                var column = screen.Columns[i];
                var columnId = columns[i].Id;
                var value = column.GetValue(context);
                processedValues[columnId] = value;
            }

            rows.Add(ScreenRowDto.Create(id, processedValues));
        }

        return rows.ToArray();
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
}