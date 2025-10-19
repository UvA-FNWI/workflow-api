namespace UvA.Workflow.Entities.Domain;

public class Step
{
    public string Name { get; set; } = null!;
    public BilingualString? Title { get; set; }
    public BilingualString DisplayTitle => Title ?? Name;

    [YamlMember(Alias = "children")] public string[] ChildNames { get; set; } = [];

    [YamlIgnore] public Step[] Children { get; set; } = [];

    public Condition? Condition { get; set; }
    public List<Action> Actions { get; set; } = [];
    public Condition? Ends { get; set; }

    public Dictionary<string, Question> Properties { get; set; } = new();

    [YamlIgnore]
    public IEnumerable<Lookup> Lookups =>
    [
        ..Ends?.Properties ?? [],
        ..Condition?.Properties ?? [],
        ..Children.SelectMany(c => c.Lookups)
    ];

    [YamlIgnore] public string? EndEvent => Ends?.Event?.Id;

    public DateTime? GetEndDate(WorkflowInstance instance)
    {
        if (Ends?.Event != null)
            return instance.Events.GetValueOrDefault(Ends.Event.Id)?.Date;
        if (Children.Any())
        {
            var dates = Children.Select(c => c.GetEndDate(instance)).ToArray();
            if (dates.Any(d => d == null))
                return null;
            return dates.Max();
        }

        return null;
    }

    public bool HasEnded(ObjectContext context)
    {
        if (Ends != null)
            return Ends.IsMet(context);
        if (Children.Any())
            return Children.All(c => c.HasEnded(context));
        return false;
    }
}