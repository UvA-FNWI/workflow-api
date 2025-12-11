using UvA.Workflow.Api.Screens.Dtos;
using UvA.Workflow.Events;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Screens;

public class ScreenDataService(
    ModelService modelService,
    IWorkflowInstanceRepository repository)
{
    public async Task<ScreenDataDto> GetScreenData(string screenName, string workflowDefinition, CancellationToken ct)
    {
        // Get the screen definition
        var screen = GetScreen(screenName, workflowDefinition);
        if (screen == null)
            throw new ArgumentException($"Screen '{screenName}' not found for entity type '{workflowDefinition}'");

        // Build projection based on screen columns
        var projection = BuildProjection(screen, workflowDefinition);
        var rawData = await repository.GetAllByType(workflowDefinition, projection, ct);

        // Process the data and apply templates/expressions
        var columns = screen.Columns.Select(ScreenColumnDto.Create).ToArray();
        var rows = ProcessRows(rawData, screen, workflowDefinition, columns);

        return ScreenDataDto.Create(screen, columns, rows);
    }

    private Screen? GetScreen(string screenName, string workflowDefinition)
    {
        if (!modelService.WorkflowDefinitions.TryGetValue(workflowDefinition, out var entity))
            return null;

        return entity.Screens.GetOrDefault(screenName);
    }

    private Dictionary<string, string> BuildProjection(Screen screen, string workflowDefinition)
    {
        if (!modelService.WorkflowDefinitions.TryGetValue(workflowDefinition, out var entity))
            throw new ArgumentException($"Entity type '{workflowDefinition}' not found");

        var projection = new Dictionary<string, string>();

        foreach (var column in screen.Columns)
        {
            if (column.CurrentStep)
            {
                projection["CurrentStep"] = "$CurrentStep";
            }
            else if (!string.IsNullOrEmpty(column.Property))
            {
                // Use WorkflowDefinition.GetKey to get the correct MongoDB path
                var mongoPath = entity.GetKey(column.Property.Split('.')[0]);
                var propertyName = column.Property.Split('.')[0];

                projection.TryAdd(propertyName, mongoPath);
            }

            // If column has templates, we need to include their properties
            if (column.ValueTemplate != null)
            {
                foreach (var prop in column.ValueTemplate.Properties)
                {
                    AddLookupToProjection(projection, prop, entity);
                }
            }
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
        List<Dictionary<string, BsonValue>> rawData,
        Screen screen,
        string workflowDefinition,
        ScreenColumnDto[] columns
    )
    {
        var rows = new List<ScreenRowDto>();

        foreach (var rawRow in rawData)
        {
            var id = rawRow.GetValueOrDefault("_id")?.ToString() ?? "Unknown";
            var processedValues = new Dictionary<int, object?>();

            // Process each column and use its ID as the key
            for (int i = 0; i < screen.Columns.Length; i++)
            {
                var column = screen.Columns[i];
                var columnId = columns[i].Id;
                var value = ProcessColumnValue(rawRow, column, workflowDefinition, id);
                processedValues[columnId] = value;
            }

            rows.Add(ScreenRowDto.Create(id, processedValues));
        }

        return rows.ToArray();
    }

    private object? ProcessColumnValue(
        Dictionary<string, BsonValue> rawRow,
        Column column,
        string workflowDefinition,
        string instanceId
    )
    {
        if (column.CurrentStep)
        {
            // Return current step value or default
            return rawRow.GetStringValue("CurrentStep") ?? column.Default ?? "Draft";
        }

        if (column.ValueTemplate != null)
        {
            // Process template - create a context and evaluate the template
            var context = CreateContextFromRawRow(rawRow, workflowDefinition, instanceId);
            return column.ValueTemplate.Execute(context);
        }

        if (!string.IsNullOrEmpty(column.Property))
        {
            // Get property value from raw data
            var value = GetNestedPropertyValue(rawRow, column.Property);
            if (value != null && !value.IsBsonNull)
            {
                return BsonConversionTools.ConvertBasicBsonValue(value);
            }
        }

        return column.Default;
    }

    private ObjectContext CreateContextFromRawRow(Dictionary<string, BsonValue> rawRow, string workflowDefinition,
        string instanceId)
    {
        // Create a minimal WorkflowInstance for context creation
        // Convert the projected properties back to the expected Properties format
        var properties = new Dictionary<string, BsonValue>();

        foreach (var kvp in rawRow)
        {
            if (kvp.Key == "_id" || kvp.Key == "CurrentStep" || kvp.Key.EndsWith("Event")) // TODO this is a bit iffy?
                continue;

            // The key is the property name, value is the BsonValue from $Properties.{key}
            properties[kvp.Key] = kvp.Value;
        }

        var instance = new WorkflowInstance
        {
            Id = instanceId,
            WorkflowDefinition = workflowDefinition,
            Properties = properties,
            Events = new Dictionary<string, InstanceEvent>(),
            CurrentStep = rawRow.GetStringValue("CurrentStep")
        };

        return modelService.CreateContext(instance);
    }

    private BsonValue? GetNestedPropertyValue(Dictionary<string, BsonValue> data, string propertyPath)
    {
        var parts = propertyPath.Split('.');
        var rootProperty = parts[0];

        if (!data.TryGetValue(rootProperty, out var rootValue))
            return null;

        // If only one part, return the root value
        if (parts.Length == 1)
            return rootValue;

        // Use shared utility to navigate the remaining path
        return BsonConversionTools.NavigateNestedBsonValue(rootValue, parts.Skip(1));
    }
}