using UvA.Workflow.Expressions;
using Date = UvA.Workflow.Entities.Domain.Conditions.Date;

namespace UvA.Workflow.Entities.Domain;

public enum StepHierarchyMode
{
    Sequential,
    Parallel
}

public class Step
{
    /// <summary>
    /// Internal name of the step
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Localized title of the step as shown to user
    /// </summary>
    public BilingualString? Title { get; set; }

    public BilingualString DisplayTitle => Title ?? Name;

    /// <summary>
    /// Determines how the child steps of this step are handled
    /// </summary>
    public StepHierarchyMode HierarchyMode { get; set; }

    /// <summary>
    /// Child steps of this step
    /// </summary>
    [YamlMember(Alias = "children")]
    public string[] ChildNames { get; set; } = [];

    [YamlIgnore] public Step[] Children { get; set; } = [];

    /// <summary>
    /// Condition requires for this step to start
    /// </summary>
    public Condition? Condition { get; set; }

    /// <summary>
    /// Actions that are possible while this step is active
    /// </summary>
    public List<Action> Actions { get; set; } = [];

    /// <summary>
    /// Condition that determines when the step ends 
    /// </summary>
    public Condition? Ends { get; set; }

    /// <summary>
    /// Properties related to this step. These will become properties of the corresponding entity 
    /// </summary>
    public Dictionary<string, Question> Properties { get; set; } = new();

    public IEnumerable<Lookup> Lookups =>
    [
        ..Ends?.Properties ?? [],
        ..Condition?.Properties ?? [],
        ..Children.SelectMany(c => c.Lookups)
    ];

    public string? EndEvent => Ends?.Event?.Id;

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

    public DateTime? GetDeadline(WorkflowInstance instance, ModelService modelService)
    {
        if (Condition?.Deadline == null)
            return null;
        var exp = ExpressionParser.Parse(Condition.Deadline.Source);
        var context = ObjectContext.Create(instance, modelService);
        return exp.Execute(context) as DateTime?;
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