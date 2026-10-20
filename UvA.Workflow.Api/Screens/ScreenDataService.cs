using UvA.Workflow.Api.Screens.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;

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

        var canBulkEdit = await instanceAuthorizationFilterService.HasEditableInstances(workflowDefinition, ct);

        // When the screen is grouped, return groups instead of a flat row list
        if (screen.Grouping != null)
        {
            var groups = BuildGroups(definition, contexts, screen, columns);
            return ScreenDataDto.Create(screen, definition, columns, [], canBulkEdit, groups, canCreateInstance);
        }

        var rows = ProcessRows(definition, contexts, screen, columns);
        return ScreenDataDto.Create(screen, definition, columns, rows, canBulkEdit,
            canCreateInstance: canCreateInstance);
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
            if (column.Type == ColumnType.CurrentStep)
                projection["CurrentStep"] = "$CurrentStep";
            if (column.Type == ColumnType.RelatedUsers)
                foreach (var relatedUser in GetDefaultRelatedUsers(entity))
                    AddLookupToProjection(projection, new PropertyLookup(relatedUser.Property), entity);
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
                // ObjectContext computes LastEvent from Events, which LoadData always projects.
                if (propertyName == "LastEvent")
                    break;

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
        WorkflowDefinition definition,
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
                object? value = columns[i].ColumnType switch
                {
                    ColumnType.CurrentStep => StepProgressResolver.ResolveProgress(definition,
                        column.GetValue(context) as string ?? "",
                        context),
                    ColumnType.RelatedUsers => GetRelatedUsersScreen(definition, context),
                    _ => column.GetValue(context)
                };
                processedValues[columnId] = value;
            }

            rows.Add(ScreenRowDto.Create(id, processedValues));
        }

        return rows.ToArray();
    }

    public static ScreenRelatedUserDto[] GetRelatedUsersScreen(WorkflowDefinition definition, ObjectContext context) =>
        GetDefaultRelatedUsers(definition)
            .SelectMany(relatedUser => GetUsersForRole(relatedUser, context))
            .GroupBy(user => user.DisplayName)
            .Select(group => new ScreenRelatedUserDto(
                group.Key,
                group.SelectMany(user => user.Roles).ToArray(),
                group.First().Organisation))
            .ToArray();

    private static RelatedUser[] GetDefaultRelatedUsers(WorkflowDefinition definition) => definition.RelatedUserGroups
        .Where(group => group.Name == "default")
        .SelectMany(group => group.Users)
        .ToArray();

    private static IEnumerable<ScreenRelatedUserDto> GetUsersForRole(RelatedUser relatedUser, ObjectContext context)
    {
        var value = context.Get(relatedUser.Property);
        var users = value switch
        {
            InstanceUser user => [user],
            InstanceUser[] userArray => userArray.OfType<InstanceUser>(),
            _ => []
        };

        return users.Select(user => new ScreenRelatedUserDto(
            user.DisplayName,
            [relatedUser.DisplayTitle],
            user.Organization?.Name));
    }

    private async Task<List<ObjectContext>> LoadData(Screen screen, string workflowDefinition, CancellationToken ct)
    {
        var definition = modelService.WorkflowDefinitions[workflowDefinition];
        var hasProgressColumn = screen.Columns.Any(column => column.Type == ColumnType.CurrentStep);
        var progressLookups = hasProgressColumn
            ? definition.ProgressLookups.ToArray()
            : [];

        // Build projection based on screen columns, always including CurrentStep for grouping
        var projection = BuildProjection(screen.Columns, workflowDefinition, progressLookups);
        projection.TryAdd("CurrentStep", "$CurrentStep");
        projection.TryAdd("Events", "$Events");
        projection.TryAdd("CreateDate", "$CreatedOn");

        // Build authorization filter to restrict instances to those the user can view
        var authorizationFilter =
            await instanceAuthorizationFilterService.BuildAuthorizationFilter(workflowDefinition, RoleAction.View, ct);

        var rawData = await repository.GetAllByType(workflowDefinition, projection, authorizationFilter, ct);
        var contexts = rawData.Select(row => modelService.CreateContext(workflowDefinition, row)).ToList();
        var relatedUserLookups = screen.Columns.Any(c => c.Type == ColumnType.RelatedUsers)
            ? GetDefaultRelatedUsers(definition).Select(u => (Lookup)new PropertyLookup(u.Property)).ToArray()
            : [];

        // Add related properties as needed
        await instanceService.Enrich(definition,
            contexts, screen.Columns.SelectMany(c => c.Properties).Concat(progressLookups).Concat(relatedUserLookups),
            ct, false);

        return contexts;
    }

    /// <summary>
    /// Partitions the loaded instances into the screen's configured groups, keyed by their current
    /// workflow step. All configured groups are always included (even when empty); instances whose
    /// step does not match any group are dropped.
    /// </summary>
    private ScreenGroupDto[] BuildGroups(
        WorkflowDefinition definition,
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
            string? groupName;
            while (!stepGroupMapping.TryGetValue(stepValue, out groupName))
            {
                stepValue = definition.AllSteps.FirstOrDefault(s => s.Name == stepValue)?.ParentStep?.Name;
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
            .Where(group => group.Enabled != false)
            .Select(g => new ScreenGroupDto(
                g.Name,
                g.Title,
                ProcessRows(definition, groupedContexts.TryGetValue(g.Name, out var ctx) ? ctx : [], screen, columns)))
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
}