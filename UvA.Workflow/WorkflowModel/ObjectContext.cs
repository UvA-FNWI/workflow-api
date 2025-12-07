using System.Collections;
using UvA.Workflow.Persistence;
using UvA.Workflow.Tools;

namespace UvA.Workflow.Entities.Domain;

public class ObjectContext(Dictionary<Lookup, object?> values)
{
    public Dictionary<Lookup, object?> Values { get; } = values;

    public object? Get(Lookup path)
    {
        if (path == "now")
            return DateTime.Now;
        if (Values.TryGetValue(path, out var res))
            return res;
        var parts = path.ToString().Split('.');
        res = Values.GetValueOrDefault(parts[0]);
        foreach (var part in parts.Skip(1))
        {
            if (res is null)
                return null;
            var type = res.GetType();
            if (type.IsArray)
                res = ((IEnumerable)res).Cast<object>().Select(s => s.GetType().GetProperty(part)?.GetValue(s))
                    .ToArray();
            else
                res = res.GetType().GetProperty(part)?.GetValue(res);
        }

        return res;
    }

    public static ObjectContext Create(WorkflowInstance instance, ModelService modelService)
    {
        var dict = new Dictionary<Lookup, object?>();
        foreach (var (k, v) in instance.Properties)
        {
            // Only add property values that are present in the workflow definition
            var propertyDefinition = modelService.GetQuestion(instance, k);
            if (propertyDefinition != null)
            {
                dict.Add(k, GetValue(v, propertyDefinition));
            }
        }

        dict.Add("Id", instance.Id);
        dict.Add("CurrentStep", instance.CurrentStep);
        dict.Add("CreateDate", instance.CreatedOn);

        foreach (var ev in instance.Events.Values.Where(e => e.Date != null))
            dict.Add(ev.Id + "Event", ev.Date);
        return new ObjectContext(dict);
    }

    public static object? GetValue(BsonValue? answer, PropertyDefinition propertyDefinition)
        => GetValue(answer, propertyDefinition.DataType, propertyDefinition);

    public static object? GetValue(BsonValue? answer, DataType type, PropertyDefinition? question = null)
    {
        if (answer is null or BsonNull) return null;

        return type switch
        {
            DataType.User when question!.IsArray => answer.AsBsonArray
                .Select(u => BsonSerializer.Deserialize<User>(u.AsBsonDocument)).ToArray(),
            DataType.User => BsonSerializer.Deserialize<User>(answer.AsBsonDocument),
            DataType.Currency => BsonSerializer.Deserialize<CurrencyAmount>(answer.AsBsonDocument),
            DataType.File => BsonSerializer.Deserialize<ArtifactInfo>(answer.AsBsonDocument),
            DataType.Reference when question?.WorkflowDefinition?.IsEmbedded == true => answer.AsBsonDocument,
            DataType.Reference => answer.AsString,
            DataType.Date or DataType.DateTime => answer.AsBsonDateTime.ToLocalTime(),
            DataType.String or DataType.Choice => BsonConversionTools.ConvertBasicBsonValue(answer),
            DataType.Int => BsonConversionTools.ConvertBasicBsonValue(answer),
            DataType.Double => BsonConversionTools.ConvertBasicBsonValue(answer),
            _ when question!.IsArray => answer.AsBsonArray.Select(r => r.AsString).ToArray(),
            _ => throw new NotImplementedException()
        };
    }
}