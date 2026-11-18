using UvA.Workflow.Expressions;

namespace UvA.Workflow.WorkflowModel;

public partial class WorkflowDefinition
{
    [YamlIgnore]
    [JsonIgnore]
    public IEnumerable<Lookup> PermissionAndStepLookups => Roles.SelectMany(role => role.Actions).Concat(AllActions)
        .SelectMany(action => action.Condition?.Properties ?? [])
        .Concat(AllSteps.SelectMany(step =>
            (step.Condition?.Properties ?? []).Concat(step.Ends?.Properties ?? [])
            .Concat(step.Deadline is { Type: DeadlineType.Hard } deadline
                ? ExpressionParser.Parse(deadline.Date).Properties
                : [])));

    /// <summary>Replaces computed lookups with the stored inputs needed to evaluate them.</summary>
    public IEnumerable<Lookup> GetSourceLookups(IEnumerable<Lookup> lookups)
    {
        foreach (var lookup in lookups)
        {
            if (lookup is PropertyLookup property &&
                Properties.GetOrDefault(property.Parts[0]) is { IsComputed: true } computed)
            {
                var sources = computed.ComputedExpression!.Properties;
                if (property.Parts.Length > 1 && computed is WorkflowPropertyDefinition)
                    sources = sources.Select(source => source is PropertyLookup input
                        ? new PropertyLookup(input.Property + "." + string.Join('.', property.Parts.Skip(1)))
                        : source);
                foreach (var source in GetSourceLookups(sources))
                    yield return source;
            }
            else if (lookup is ComplexLookup complex)
            {
                foreach (var source in GetSourceLookups(complex.Arguments.SelectMany(arg => arg.Properties)))
                    yield return source;
            }
            else
                yield return lookup;
        }
    }

    /// <summary>Projects stored inputs and metadata needed to evaluate the requested lookups.</summary>
    public Dictionary<string, string> GetProjection(IEnumerable<Lookup> lookups)
    {
        var projection = new Dictionary<string, string>();
        foreach (var lookup in GetSourceLookups(lookups).OfType<PropertyLookup>())
        {
            var property = lookup.Parts[0];
            if (property == "LastEvent" || Events.Any(ev =>
                    property == ev.Name + "Event" || property == ev.Name + "EventActive"))
            {
                projection.TryAdd("Events", "$Events");
                projection.TryAdd("CreateDate", "$CreatedOn");
            }
            else
                projection.TryAdd(property, GetKey(property));
        }

        if (projection.Count == 0)
            projection.Add("_id", "$_id");
        return projection;
    }

    // Computed reads use the existing single reference-loading pass.
    internal void ValidateReadLookups(IEnumerable<Lookup> lookups, bool allowReference = true,
        bool allowAssessment = true)
    {
        var visited = new HashSet<(WorkflowDefinition, PropertyLookup, bool)>();
        Validate(this, lookups, allowReference);

        void Validate(WorkflowDefinition definition, IEnumerable<Lookup> inputs, bool canLoadReference)
        {
            foreach (var lookup in definition.GetSourceLookups(inputs).OfType<PropertyLookup>())
            {
                if (!allowAssessment && !definition.Properties.Contains("Assessment") &&
                    lookup.Parts[0] == "Assessment")
                    throw new Exception("Assessment inputs require enrichment");
                if (!visited.Add((definition, lookup, canLoadReference)) || lookup.Parts.Length < 2 ||
                    definition.Properties.GetOrDefault(lookup.Parts[0]) is not WorkflowPropertyDefinition
                    {
                        WorkflowDefinition: not null
                    } property)
                    continue;

                var isReference = property is ReferencePropertyDefinition;
                if (isReference && (definition.IsEmbedded || !canLoadReference))
                    throw new Exception($"'{definition.Name}.{lookup.Property}' requires " +
                                        (definition.IsEmbedded
                                            ? "reference loading inside an embedded object"
                                            : "more than one reference hop"));

                Validate(property.WorkflowDefinition, [(Lookup)string.Join('.', lookup.Parts.Skip(1))],
                    canLoadReference && !isReference);
            }
        }
    }
}