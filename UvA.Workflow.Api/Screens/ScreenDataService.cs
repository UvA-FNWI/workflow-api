using UvA.Workflow.Api.Screens.Dtos;

namespace UvA.Workflow.Api.Screens;

public class ScreenDataService(
    ModelService modelService,
    IWorkflowInstanceRepository repository)
{
    public async Task<ScreenDataDto> GetScreenData(string screenName, string entityType, CancellationToken ct)
    {
        // Get the screen definition
        var screen = GetScreen(screenName, entityType);
        if (screen == null)
            throw new ArgumentException($"Screen '{screenName}' not found for entity type '{entityType}'");

        // Build projection based on screen columns
        var projection = BuildProjection(screen, entityType);

        // Get data from MongoDB using projection
        var rawData = await repository.GetAllByType(entityType, projection, ct);

        // Process the data and apply templates/expressions
        var columns = screen.Columns.Select((column, index) => ScreenColumnDto.Create(column, index)).ToArray();
        var rows = ProcessRows(rawData, screen, entityType, columns);

        return ScreenDataDto.Create(screen, columns, rows);
    }

    private Screen? GetScreen(string screenName, string entityType)
    {
        if (!modelService.EntityTypes.TryGetValue(entityType, out var entity))
            return null;

        return entity.Screens.GetValueOrDefault(screenName);
    }

    private Dictionary<string, string> BuildProjection(Screen screen, string entityType)
    {
        if (!modelService.EntityTypes.TryGetValue(entityType, out var entity))
            throw new ArgumentException($"Entity type '{entityType}' not found");

        var projection = new Dictionary<string, string>();

        foreach (var column in screen.Columns)
        {
            if (column.CurrentStep)
            {
                projection["CurrentStep"] = "$CurrentStep";
            }
            else if (!string.IsNullOrEmpty(column.Property))
            {
                // Use EntityType.GetKey to get the correct MongoDB path
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

    private void AddLookupToProjection(Dictionary<string, string> projection, Lookup lookup, EntityType entity)
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
        string entityType,
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
                var columnId = columns[i].Id; // Use the generated column ID
                var value = ProcessColumnValue(rawRow, column, entityType, id);
                processedValues[columnId] = value;
            }

            rows.Add(ScreenRowDto.Create(id, processedValues));
        }

        return rows.ToArray();
    }

    private object? ProcessColumnValue(
        Dictionary<string, BsonValue> rawRow,
        Column column,
        string entityType,
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
            var context = CreateContextFromRawRow(rawRow, entityType, instanceId);
            return column.ValueTemplate.Execute(context);
        }

        if (!string.IsNullOrEmpty(column.Property))
        {
            // Get property value from raw data
            var value = GetNestedPropertyValue(rawRow, column.Property);
            if (value != null && !value.IsBsonNull)
            {
                return ConvertBsonValueToObject(value);
            }
        }

        return column.Default;
    }

    private ObjectContext CreateContextFromRawRow(Dictionary<string, BsonValue> rawRow, string entityType,
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
            EntityType = entityType,
            Properties = properties,
            Events = new Dictionary<string, InstanceEvent>(),
            CurrentStep = rawRow.GetStringValue("CurrentStep")
        };

        return modelService.CreateContext(instance);
    }

    private BsonValue? GetNestedPropertyValue(Dictionary<string, BsonValue> data, string propertyPath)
    {
        var parts = propertyPath.Split('.');

        // For a property like "Student.DisplayName", we need to look in the "Student" key first
        var rootProperty = parts[0];
        if (!data.TryGetValue(rootProperty, out var current))
            return null;

        // Navigate through the nested properties
        foreach (var part in parts.Skip(1))
        {
            if (current?.IsBsonDocument == true)
            {
                var doc = current.AsBsonDocument;
                if (!doc.TryGetValue(part, out current))
                    return null;
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    private object? ConvertBsonValueToObject(BsonValue bsonValue)
    {
        return bsonValue.BsonType switch
        {
            BsonType.String => bsonValue.AsString,
            BsonType.Int32 => bsonValue.AsInt32,
            BsonType.Int64 => bsonValue.AsInt64,
            BsonType.Double => bsonValue.AsDouble,
            BsonType.Boolean => bsonValue.AsBoolean,
            BsonType.DateTime => bsonValue.ToUniversalTime(),
            BsonType.ObjectId => bsonValue.AsObjectId.ToString(),
            BsonType.Null => null,
            BsonType.Document => bsonValue.AsBsonDocument.ToDictionary(
                kvp => kvp.Name,
                kvp => ConvertBsonValueToObject(kvp.Value)
            ),
            BsonType.Array => bsonValue.AsBsonArray.Select(ConvertBsonValueToObject).ToArray(),
            _ => bsonValue.ToString()
        };
    }
}