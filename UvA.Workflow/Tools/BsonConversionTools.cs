namespace UvA.Workflow.Tools;

public static class BsonConversionTools
{
    /// <summary>
    /// Converts basic BSON types to their corresponding .NET objects.
    /// This handles primitive types and basic collections but not domain-specific types.
    /// </summary>
    /// <param name="bsonValue">The BSON value to convert</param>
    /// <returns>The converted object or null</returns>
    public static object? ConvertBasicBsonValue(BsonValue? bsonValue)
    {
        if (bsonValue == null || bsonValue.IsBsonNull)
            return null;

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
                kvp => ConvertBasicBsonValue(kvp.Value)
            ),
            BsonType.Array => bsonValue.AsBsonArray.Select(ConvertBasicBsonValue).ToArray(),
            _ => bsonValue.ToString()
        };
    }

    /// <summary>
    /// Navigates through nested BSON document properties using a dot-separated path.
    /// </summary>
    /// <param name="startValue">The starting BSON value to navigate from</param>
    /// <param name="pathParts">The property path parts to navigate through</param>
    /// <returns>The final BSON value or null if navigation fails</returns>
    public static BsonValue? NavigateNestedBsonValue(BsonValue? startValue, IEnumerable<string> pathParts)
    {
        var current = startValue;
        
        foreach (var part in pathParts)
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
}
