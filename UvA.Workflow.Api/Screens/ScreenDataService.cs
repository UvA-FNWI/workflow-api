using UvA.Workflow.Api.Screens.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.WorkflowModel.Conditions;

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
    /// the rows are partitioned into groups by condition or current workflow step (and the flat row list
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
        var contexts = await LoadData(screen, definition, ct);

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
        IEnumerable<Lookup> additionalLookups)
    {
        if (!modelService.WorkflowDefinitions.TryGetValue(workflowDefinition, out var entity))
            throw new ArgumentException($"Entity type '{workflowDefinition}' not found");

        var projection = new Dictionary<string, string>();

        foreach (var column in columns)
        {
            if (column.CurrentStep)
                projection[ContextKeys.CurrentStep] = entity.GetKey(ContextKeys.CurrentStep);
            foreach (var prop in column.Lookups)
                AddLookupToProjection(projection, prop, entity);
        }

        foreach (var lookup in additionalLookups)
            AddLookupToProjection(projection, lookup, entity);

        return projection;
    }

    private void AddLookupToProjection(Dictionary<string, string> projection, Lookup lookup, WorkflowDefinition entity)
    {
        switch (lookup)
        {
            case PropertyLookup propertyLookup:
                var propertyName = propertyLookup.Property.Split('.')[0];
                // These values are computed by ObjectContext from the projected data.
                if (ContextKeys.IsComputed(propertyName))
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
            Dictionary<int, ScreenCellDisplayDto>? display = null;

            // Process each column and use its ID as the key
            for (int i = 0; i < screen.Columns.Length; i++)
            {
                var column = screen.Columns[i];
                var columnId = columns[i].Id;
                var rule = column.Overrides.FirstOrDefault(rule => rule.Condition.IsMet(context));
                if (columns[i].IsCurrentStep)
                {
                    processedValues[columnId] = rule != null
                        ? new ProgressInformationDto(rule.TextTemplate.Apply(context), rule.Color)
                        : StepProgressResolver.ResolveProgress(definition, column.GetValue(context) as string ?? "",
                            context);
                    continue;
                }

                processedValues[columnId] = column.GetValue(context);
                if (rule != null)
                    (display ??= [])[columnId] = new ScreenCellDisplayDto(rule.TextTemplate.Apply(context), rule.Color);
            }

            rows.Add(new ScreenRowDto(id, processedValues, display));
        }

        return rows.ToArray();
    }

    private async Task<List<ObjectContext>> LoadData(Screen screen, WorkflowDefinition workflowDefinition,
        CancellationToken ct)
    {
        var definitions = modelService.WorkflowDefinitions.Values.Where(definition =>
        {
            if (!workflowDefinition.GroupChildScreens)
                return definition == workflowDefinition;

            if (definition.IsEmbedded)
                return false;

            for (var current = definition; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current, workflowDefinition))
                    return true;
            }

            return false;
        });

        var contexts = new List<ObjectContext>();
        var groupLookups = screen.Grouping?.Groups.SelectMany(group => group.Condition?.Properties ?? []).ToArray()
                           ?? [];

        foreach (var definition in definitions)
        {
            var progressLookups = screen.Columns.Any(column => column.CurrentStep)
                ? definition.ProgressLookups.ToArray()
                : [];
            var additionalLookups = progressLookups.Concat(groupLookups);

            var projection = BuildProjection(screen.Columns, definition.Name,
                additionalLookups.Concat(definition.StopWhen?.Properties ?? []));
            projection.TryAdd(ContextKeys.CurrentStep, definition.GetKey(ContextKeys.CurrentStep));
            projection.TryAdd("Events", "$Events");
            projection.TryAdd(ContextKeys.CreateDate, definition.GetKey(ContextKeys.CreateDate));

            var authorizationFilter =
                await instanceAuthorizationFilterService.BuildAuthorizationFilter(
                    definition.Name, RoleAction.View, ct);

            var rawData = await repository.GetAllByType(
                definition.Name, projection, authorizationFilter, ct);

            var rows = rawData
                .Select(row => modelService.CreateContext(definition.Name, row))
                .ToList();

            await instanceService.Enrich(
                definition,
                rows,
                screen.Columns.SelectMany(column => column.Lookups).Concat(additionalLookups),
                ct,
                false);

            if (workflowDefinition.GroupChildScreens)
            {
                foreach (var row in rows)
                    row.Values["Type"] = definition.Name;
            }

            contexts.AddRange(rows);
        }

        return contexts;
    }

    /// <summary>
    /// Partitions instances by the first matching condition, then by current workflow step.
    /// All configured groups are included, even when empty. Unmatched instances are dropped.
    /// </summary>
    private ScreenGroupDto[] BuildGroups(
        WorkflowDefinition definition,
        ICollection<ObjectContext> contexts,
        Screen screen,
        ScreenColumnDto[] columns)
    {
        var groups = screen.Grouping!.Groups;
        var conditionalGroups = groups.Where(group => group.Condition != null).ToArray();
        var stepGroupMapping = BuildStepGroupMapping(groups);

        var groupedContexts = new Dictionary<string, List<ObjectContext>>(StringComparer.OrdinalIgnoreCase);

        foreach (var context in contexts)
        {
            var groupName = conditionalGroups.FirstOrDefault(group => group.Condition.IsMet(context))?.Name;
            if (groupName == null)
            {
                var stepValue = context.Get(ContextKeys.CurrentStep)?.ToString() ?? EmptyStepId;
                while (!stepGroupMapping.TryGetValue(stepValue, out groupName))
                {
                    stepValue = definition.AllSteps.FirstOrDefault(s => s.Name == stepValue)?.ParentStep?.Name;
                    if (stepValue == null)
                        break;
                }
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
        return groups
            .Select(g => new ScreenGroupDto(
                g.Name,
                g.Title,
                ProcessRows(definition, groupedContexts.TryGetValue(g.Name, out var ctx) ? ctx : [], screen, columns)))
            .ToArray();
    }

    private static Dictionary<string, string> BuildStepGroupMapping(StepGroup[] groups)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups.Where(group => group.Condition == null))
        {
            foreach (var step in group.Steps)
            {
                mapping[step ?? EmptyStepId] = group.Name;
            }
        }

        return mapping;
    }
}