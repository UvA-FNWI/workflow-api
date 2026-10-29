using UvA.Workflow.Expressions;
using UvA.Workflow.Migrations;
using Action = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.WorkflowModel;

/// <summary>
/// Represents a type of object ("entity") in the workflow system
/// </summary>
public class WorkflowDefinition : INamed, IDeclaredKeys
{
    [YamlIgnore] public HashSet<string> DeclaredKeys { get; set; } = new();

    /// <summary>
    /// Short internal name of the entity type
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Localized title of the entity type
    /// </summary>
    public BilingualString? Title { get; set; }

    /// <summary>
    /// Localized plural of the entity type title
    /// </summary>
    public BilingualString TitlePlural { get; set; } = null!;

    public BilingualString DisplayTitle => Title ?? Name;

    /// <summary>
    /// Integer indicating the tab order in the instance page
    /// </summary>
    public int? Index { get; set; }

    /// <summary>
    /// Always show this entity type to the user, even if the user has no rights 
    /// </summary>
    public bool IsAlwaysVisible { get; set; }

    /// <summary>
    /// Name of the entity type this type inherits from
    /// </summary>
    public string? InheritsFrom { get; set; }

    /// <summary>
    /// Template for the title of an instance
    /// </summary>
    public string? InstanceTitle { get; set; }

    public Template? InstanceTitleTemplate => field ??= Template.Create(InstanceTitle);

    /// <summary>
    /// Dictionary of properties for this entity type
    /// </summary>
    public List<PropertyDefinition> Properties { get; set; } = new();

    [YamlIgnore] public List<PropertyDefinition> ComputedProperties { get; } = [];

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
    internal void ValidateReadLookups(IEnumerable<Lookup> lookups, bool allowReference = true)
    {
        var visited = new HashSet<(WorkflowDefinition, PropertyLookup, bool)>();
        Validate(this, lookups, allowReference);

        void Validate(WorkflowDefinition definition, IEnumerable<Lookup> inputs, bool canLoadReference)
        {
            foreach (var lookup in definition.GetSourceLookups(inputs).OfType<PropertyLookup>())
            {
                if (!visited.Add((definition, lookup, canLoadReference)) || lookup.Parts.Length < 2 ||
                    definition.Properties.GetOrDefault(lookup.Parts[0]) is not WorkflowPropertyDefinition
                        { WorkflowDefinition: not null } property)
                    continue;

                var isReference = property is ReferencePropertyDefinition;
                if (isReference && (definition.IsEmbedded || !canLoadReference))
                    throw new Exception($"'{definition.Name}.{lookup.Property}' requires " +
                        (definition.IsEmbedded ? "reference loading inside an embedded object" : "more than one reference hop"));

                Validate(property.WorkflowDefinition, [(Lookup)string.Join('.', lookup.Parts.Skip(1))],
                    canLoadReference && !isReference);
            }
        }
    }

    /// <summary>
    /// Dictionary of event definitions for this entity type
    /// </summary>
    public List<EventDefinition> Events { get; set; } = new();

    /// <summary>
    /// List of actions for this entity type
    /// </summary>
    public List<Action> GlobalActions { get; set; } = [];

    public IEnumerable<Action> AllActions => GlobalActions.Concat(AllSteps.SelectMany(s => s.Actions));

    /// <summary>
    /// List of roles for this entity type. Loaded from the Roles folder of the entity type definition.
    /// </summary>
    [YamlIgnore]
    public List<Role> Roles { get; set; } = [];

    /// <summary>
    /// List of step names for this entity type
    /// </summary>
    [YamlMember(Alias = "steps")]
    public List<string> StepNames { get; set; } = [];

    /// <summary>
    /// List of fields for this entity type
    /// </summary>
    public Field[] Fields { get; set; } = [];

    public InfoCard[] InfoCards { get; set; } = [];

    [YamlIgnore]
    public RelatedUser[] RelatedUsers => InfoCards
        .Where(card => card.Enabled && card.Type == InfoCardType.RelatedUsers)
        .SelectMany(card => card.Groups)
        .SelectMany(group => group.Users)
        .ToArray();

    [YamlIgnore]
    public RelatedUser[] EditableRelatedUsers => InfoCards
        .Where(card => card.Enabled && card.Type == InfoCardType.RelatedUsers)
        .SelectMany(card => card.Groups.Where(group => group.AllowEditing))
        .SelectMany(group => group.Users)
        .ToArray();

    /// <summary>
    /// Indicated whether this entity type is stored as an embedded document in the parent instance
    /// </summary>
    public bool IsEmbedded { get; set; }

    /// <summary>
    /// Data to be automatically created on app start. Will match existing data by external ID
    /// </summary>
    public Dictionary<string, string>[]? SeedData { get; set; }

    [YamlMember(Alias = "assessments")] public AssessmentConfiguration? AssessmentConfiguration { get; set; }

    [YamlIgnore] public ModelParser ModelParser { get; set; } = null!;
    [YamlIgnore] public string SourceFolder { get; set; } = null!;

    /// <summary>
    /// Value sets declared in this definition's own ValueSets folder, plus the ones it inherits.
    /// </summary>
    [YamlIgnore]
    public List<ValueSet> ValueSets { get; set; } = [];

    [YamlIgnore] public List<Form> Forms { get; set; } = null!;
    [YamlIgnore] public List<Step> AllSteps { get; set; } = null!;
    [YamlIgnore] public List<Screen> Screens { get; set; } = null!;
    [YamlIgnore] public List<Step> Steps { get; set; } = [];
    [YamlIgnore] public List<TemplateMessage> Emails { get; set; } = null!;
    [YamlIgnore] public List<ConfiguredMigration> Migrations { get; set; } = [];
    [YamlIgnore] public WorkflowDefinition? Parent { get; set; }

    [YamlIgnore]
    public IEnumerable<Lookup> ProgressLookups => AllSteps
        .SelectMany(step => step.Progress)
        .SelectMany(progress => progress.Lookups)
        .Distinct();

    private static IEnumerable<Step> GetWalkSteps(Step s) =>
        s.IsAlongside
            ? [s]
            : s.Children.Any() && s.HierarchyMode == StepHierarchyMode.Sequential
                ? s.Children.SelectMany(GetWalkSteps)
                : [s];

    /// <summary>
    /// Flattened walk: sequential children replace their parent, except an alongside parent stays one position.
    /// </summary>
    public IEnumerable<Step> WalkSteps => Steps.SelectMany(GetWalkSteps);

    public DataType GetDataType(string property)
    {
        if (property is "LastEvent" or "CreateDate")
            return DataType.DateTime;
        if (Properties.TryGetValue(property, out var prop))
            return prop.DataType;
        if (property.EndsWith("Event") && Events.Contains(property[..^5]))
            return DataType.DateTime;
        return DataType.String;
    }

    public string GetKey(string property)
    {
        if (property == "CreateDate")
            return "$CreatedOn";
        if (Properties.Contains(property))
            return $"$Properties.{property}";
        if (property.EndsWith("Event") && Events.Contains(property[..^5]))
            return $"$Events.{property[..^5]}.Date";
        return "$" + property;
    }
}

public class EventDefinition : INamed
{
    /// <summary>
    /// Name of the event
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// List of event names that are suppressed when this event occurs. Only the most recent event will be suppressed.
    /// </summary>
    public List<string>? Suppresses { get; set; }

    /// <summary>
    /// When declared on a step event, restarts the sequential parent of the declaring step; the suppression rules are generated from the effective hierarchy (see docs/reset-parent-step.md).
    /// </summary>
    public bool ResetParentStep { get; set; }

    public EventDefinition Clone() => new()
    {
        Name = Name,
        Suppresses = Suppresses == null ? null : [.. Suppresses],
        ResetParentStep = ResetParentStep
    };
}
