using System.Collections;
using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Persistence;

namespace UvA.Workflow.WorkflowModel;

public partial class ObjectContext(
    Dictionary<Lookup, object?> values,
    IReadOnlyDictionary<string, string[]>? valueSets = null)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoValueSets = new Dictionary<string, string[]>();

    public Dictionary<Lookup, object?> Values { get; } = values;

    // Keep reference IDs available for equality and collection operations.
    internal Dictionary<Lookup, object?> LoadedReferenceValues { get; } = new();
    internal HashSet<string> UnresolvedReferences { get; } = new();
    private HashSet<string> UnavailableProperties { get; } = new();

    /// <summary>
    /// Choice names per value set, referenced in expressions as <c>ValueSets.Name</c>.
    /// Kept apart from <see cref="Values"/> so value-set names cannot clash with properties or effect outputs.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> ValueSets { get; } = valueSets ?? NoValueSets;

    public string? Id => Values.GetValueOrDefault("Id")?.ToString();

    public object? Get(Lookup path)
    {
        if (path == "now")
            return DateTime.Now;
        if (path is PropertyLookup { Parts: ["ValueSets", var valueSet] })
            return ValueSets.GetValueOrDefault(valueSet);
        if (Values.TryGetValue(path, out var res))
            return res;
        var parts = path.ToString().Split('.');
        res = parts.Length > 1 && LoadedReferenceValues.TryGetValue(parts[0], out var referenced)
            ? referenced
            : Values.GetValueOrDefault(parts[0]);
        foreach (var part in parts.Skip(1))
        {
            if (res is null)
                return null;
            var type = res.GetType();
            if (res is Dictionary<Lookup, object> dict)
                res = dict.GetValueOrDefault(part);
            else if (res is Dictionary<string, object> stringDict)
                res = stringDict.GetValueOrDefault(part);
            else if (res is Dictionary<Lookup, object>[] dicts)
                res = dicts.Select(d => d.GetValueOrDefault(part)).ToArray();
            else if (res is Dictionary<string, object>[] stringDicts)
                res = stringDicts.Select(d => d.GetValueOrDefault(part)).ToArray();
            else if (type.IsArray)
                res = ((IEnumerable)res).Cast<object>().Select(s => s.GetType().GetProperty(part)?.GetValue(s))
                    .ToArray();
            else
                res = res.GetType().GetProperty(part)?.GetValue(res);
        }

        return res;
    }

    public ObjectContext WithValues(IEnumerable<KeyValuePair<Lookup, object?>> overrides,
        IReadOnlyDictionary<string, string[]>? valueSets = null)
    {
        var entries = overrides.ToArray();
        var overridden = entries.Select(entry => entry.Key).ToHashSet();
        var copy = new ObjectContext(new Dictionary<Lookup, object?>(Values), valueSets ?? ValueSets);
        foreach (var (key, value) in entries)
            copy.Values[key] = value;
        foreach (var (key, value) in LoadedReferenceValues.Where(entry => !overridden.Contains(entry.Key)))
            copy.LoadedReferenceValues[key] = value;
        copy.UnresolvedReferences.UnionWith(UnresolvedReferences.Where(name => !overridden.Contains((Lookup)name)));
        copy.UnavailableProperties.UnionWith(UnavailableProperties.Where(name => !overridden.Contains((Lookup)name)));
        return copy;
    }

    internal ObjectContext WithPropertyValues(string path, WorkflowDefinition definition)
    {
        var overrides = definition.Properties.ToDictionary(
            property => (Lookup)property.Name, property => Get($"{path}.{property.Name}"));
        var scoped = WithValues(overrides, GetValueSets(definition));
        foreach (var property in definition.Properties)
            if (IsInputUnavailable((Lookup)$"{path}.{property.Name}"))
                scoped.UnavailableProperties.Add(property.Name);
        return scoped;
    }

    internal void EnsureInputsAvailable(IEnumerable<Lookup> lookups)
    {
        foreach (var lookup in lookups)
            if (IsInputUnavailable(lookup))
                throw new InvalidWorkflowStateException(Id ?? "", "UnresolvedReference",
                    $"Input '{lookup}' is unavailable because a referenced workflow instance could not be loaded");
    }

    private bool IsInputUnavailable(Lookup lookup) => lookup switch
    {
        PropertyLookup property => UnavailableProperties.Contains(property.Parts[0]) ||
                                   property.Parts.Length > 1 && UnresolvedReferences.Contains(property.Parts[0]),
        ComplexLookup complex => complex.Arguments.SelectMany(argument => argument.Properties).Any(IsInputUnavailable),
        _ => false
    };

    internal object? ResolveReferenceItems(object? value, IEnumerable<Lookup> sources)
    {
        if (value is not IEnumerable items || value is string)
            return value;
        var references = sources.OfType<PropertyLookup>()
            .Select(source => LoadedReferenceValues.GetValueOrDefault(source.Parts[0]))
            .SelectMany(loaded => loaded switch
            {
                Dictionary<Lookup, object?> reference => [reference],
                Dictionary<Lookup, object?>[] many => many,
                _ => Array.Empty<Dictionary<Lookup, object?>>()
            })
            .DistinctBy(reference => reference["Id"])
            .ToDictionary(reference => reference["Id"]!.ToString()!);
        return references.Count == 0
            ? value
            : items.Cast<object?>()
                .Select(item => item is string id && references.TryGetValue(id, out var reference) ? reference : item)
                .ToArray();
    }

    public static ObjectContext Create(WorkflowDefinition workflowDefinition, Dictionary<string, BsonValue> rawData,
        IEnumerable<string>? projectedProperties = null)
    {
        var dict = rawData.Where(entry => workflowDefinition.Properties.GetOrDefault(entry.Key)?.IsComputed != true)
            .ToDictionary(
                Lookup (t) => new PropertyLookup(t.Key == "_id" ? "Id" : t.Key),
                t =>
                {
                    var prop = workflowDefinition.Properties.GetOrDefault(t.Key);
                    return GetValue(t.Value, prop?.DataType ?? workflowDefinition.GetDataType(t.Key), prop);
                });

        foreach (var property in projectedProperties ?? [])
            dict.TryAdd(property == "_id" ? "Id" : property, null);

        // Normalize projected event data in the same way as a fully loaded workflow instance.
        if (rawData.GetValueOrDefault("Events") is BsonDocument || projectedProperties?.Contains("Events") == true)
        {
            var eventsDoc = rawData.GetValueOrDefault("Events") as BsonDocument ?? new BsonDocument();
            var events = eventsDoc.Elements.ToDictionary(
                element => element.Name,
                element => new InstanceEvent
                {
                    Id = element.Name,
                    Date = (element.Value as BsonDocument)?.GetValue("Date", BsonNull.Value)
                        is BsonDateTime date
                            ? date.ToUniversalTime()
                            : null
                });
            var instance = new WorkflowInstance
            {
                Id = dict.GetValueOrDefault("Id")?.ToString() ?? string.Empty,
                WorkflowDefinition = workflowDefinition.Name,
                Properties = [],
                Events = events
            };

            AddEventInformation(dict, instance, workflowDefinition);
        }
        else if (!dict.ContainsKey("LastEvent") && dict.ContainsKey("CreateDate"))
        {
            dict["LastEvent"] = dict.GetValueOrDefault("CreateDate");
        }

        var context = new ObjectContext(dict, GetValueSets(workflowDefinition));
        context.ComputeProperties(workflowDefinition);
        return context;
    }

    public static ObjectContext Create(WorkflowInstance instance, ModelService modelService,
        IReadOnlySet<string>? pendingProperties = null)
    {
        var dict = new Dictionary<Lookup, object?>();
        foreach (var (k, v) in instance.Properties)
        {
            // Only add property values that are present in the workflow definition
            var propertyDefinition = modelService.GetProperty(instance, k);
            if (propertyDefinition is { IsComputed: false })
            {
                dict.Add(k, GetValue(v, propertyDefinition));
            }
        }

        dict.Add("Id", instance.Id);
        dict.Add("CurrentStep", instance.CurrentStep);
        dict.Add("CreateDate", instance.CreatedOn.ToLocalTime());

        var workflowDef = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        foreach (var property in workflowDef.Properties.Where(property =>
                     !property.IsComputed && pendingProperties?.Contains(property.Name) != true))
            dict.TryAdd(property.Name, null);
        AddEventInformation(dict, instance, workflowDef);

        var context = new ObjectContext(dict, GetValueSets(workflowDef));
        context.ComputeProperties(workflowDef);
        return context;
    }

    /// <summary>
    /// Collects the common and definition-local value sets that expressions can reference as <c>ValueSets.Name</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> GetValueSets(WorkflowDefinition definition)
    {
        var valueSets = new Dictionary<string, string[]>();
        foreach (var set in (definition.ModelParser?.ValueSets ?? []).Concat(definition.ValueSets))
            valueSets.TryAdd(set.Name, set.Values.Select(choice => choice.Name).ToArray());
        return valueSets;
    }

    private static void AddEventInformation(
        Dictionary<Lookup, object?> dict,
        WorkflowInstance instance,
        WorkflowDefinition workflowDefinition)
    {
        dict["LastEvent"] = instance.Events.Values.Select(ev => ev.Date).Max()?.ToLocalTime()
                            ?? dict.GetValueOrDefault("CreateDate");

        foreach (var ev in workflowDefinition.Events)
        {
            dict.TryAdd(ev.Name + "Event", null);
            dict.TryAdd(ev.Name + "EventActive", false);
        }

        foreach (var ev in instance.Events.Values)
        {
            var eventKey = ev.Id + "Event";
            var activeKey = ev.Id + "EventActive";
            var isActive = EventSuppressionHelper.IsEventActive(ev.Id, instance, workflowDefinition);

            // A projected event date may already exist. Replace it with the normalized active value.
            dict[eventKey] = null;
            dict[activeKey] = isActive;

            if (ev.Date != null && isActive)
                dict[eventKey] = ev.Date.Value.ToLocalTime();
        }
    }

    public static object? GetValue(BsonValue? answer, PropertyDefinition propertyDefinition)
        => GetValue(answer, propertyDefinition.DataType, propertyDefinition);

    // TODO: is there a better way to do this?
    private static IEnumerable GetTypedArray(BsonArray array, DataType type, PropertyDefinition? property)
        => type switch
        {
            DataType.User => array.Select(r => GetValue(r, type) as InstanceUser).ToArray(),
            DataType.Currency => array.Select(r => GetValue(r, type) as CurrencyAmount).ToArray(),
            DataType.File => array.Select(r => GetValue(r, type) as ArtifactInfo).ToArray(),
            DataType.String or DataType.Choice or DataType.Reference => array.Select(r => GetValue(r, type) as string)
                .ToArray(),
            DataType.Object => array.Select(r => GetObjectValue(r, property as ObjectPropertyDefinition)).ToArray(),
            _ => array.Select(r => GetValue(r, type)).ToArray()
        };

    public static object? GetValue(BsonValue? answer, DataType type, PropertyDefinition? question = null)
    {
        if (answer is null or BsonNull) return null;

        return type switch
        {
            _ when question?.IsArray == true => answer.IsBsonArray
                ? GetTypedArray(answer.AsBsonArray, type, question)
                : GetTypedArray(new BsonArray { answer }, type, question),
            DataType.User => BsonSerializer.Deserialize<InstanceUser>(answer.AsBsonDocument),
            DataType.Currency => BsonSerializer.Deserialize<CurrencyAmount>(answer.AsBsonDocument),
            DataType.File => BsonSerializer.Deserialize<ArtifactInfo>(answer.AsBsonDocument),
            DataType.Object => GetObjectValue(answer, question as ObjectPropertyDefinition),
            DataType.Reference => answer.ToString(),
            DataType.Date or DataType.DateTime => answer.AsBsonDateTime.ToLocalTime(),
            DataType.String or DataType.Choice => BsonConversionTools.ConvertBasicBsonValue(answer),
            DataType.Int => BsonConversionTools.ConvertBasicBsonValue(answer),
            DataType.Double => BsonConversionTools.ConvertBasicBsonValue(answer),
            DataType.Check => BsonConversionTools.ConvertBasicBsonValue(answer),
            _ => throw new NotImplementedException()
        };
    }

    private static Dictionary<string, object?>? GetObjectValue(BsonValue value, ObjectPropertyDefinition? property)
    {
        if (value.IsBsonNull) return null;
        var definition = property?.WorkflowDefinition;
        if (definition == null)
            return value.AsBsonDocument.ToDictionary();
        var data = value.AsBsonDocument.ToDictionary(entry => entry.Name, entry => entry.Value);
        foreach (var source in definition.Properties.Where(source => !source.IsComputed))
            data.TryAdd(source.Name, BsonNull.Value);
        return Create(definition, data).Values.ToDictionary(entry => entry.Key.ToString()!, entry => entry.Value);
    }
}