using UvA.Workflow.Events;
using UvA.Workflow.Expressions;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.WorkflowModel;

public enum StepHierarchyMode
{
    Sequential,
    Parallel
}

public enum StepHeaderPillType
{
    Info,
    Attention,
    Success
}

public enum StatusColor
{
    Red,
    Green
}

public record Icon
{
    public string Type { get; init; } = null!;
    public StatusColor Color { get; init; } = StatusColor.Red;
}

public record ProgressInformation
{
    public StatusColor? Color { get; init; }
    public BilingualString? Text { get; init; } = null!;
    [YamlIgnore] public BilingualTemplate? ProgressTextTemplate => field ??= BilingualTemplate.Create(Text);
}

public enum StepResultsType
{
    Normal,
    AssessmentPartOverview,
    AssessmentFinalOverview
}

public class Step : INamed
{
    /// <summary>
    /// Internal name of the step
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Localized title of the step as shown to user
    /// </summary>
    public BilingualString? Title { get; set; }

    /// <summary>
    /// The progress information about the step
    /// </summary>
    public ProgressInformation? Progress { get; set; }

    /// <summary>
    /// Determines whether assessment results are shown in the step
    /// </summary>
    public StepResultsType ResultsType { get; set; }

    /// <summary>
    /// The icon of the step
    /// </summary>
    public Icon? Icon { get; set; }

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
    [YamlIgnore] public Step? ParentStep { get; set; }

    public List<StepHeaderStatusConfiguration>? HeaderStatus { get; set; }

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
    public List<PropertyDefinition> Properties { get; set; } = new();

    /// <summary>
    /// Events defined within this step. These will be merged into the workflow definition's events.
    /// </summary>
    public List<EventDefinition> Events { get; set; } = new();

    public IEnumerable<Lookup> Lookups =>
    [
        ..Ends?.Properties ?? [],
        ..Condition?.Properties ?? [],
        ..Children.SelectMany(c => c.Lookups)
    ];

    public string? EndEvent => Ends?.Event?.Id;

    public DateTime? GetEndDate(WorkflowInstance instance, WorkflowDefinition workflowDef)
    {
        if (Ends?.Logical != null && Ends?.Logical?.Children.Length > 0 &&
            Ends.Logical.Children.All(c => c.Event != null))
        {
            var dates = Ends.Logical.Children
                .Select(c => instance.Events.GetValueOrDefault(c.Event!.Id))
                .Where(evt => evt?.Date != null && EventSuppressionHelper.IsEventActive(evt.Id, instance, workflowDef))
                .Select(evt => evt!.Date!.Value)
                .ToArray();

            if (dates.Length == 0) return null;

            if (Ends.Logical.Operator == LogicalOperator.And)
                return dates.Max();
            if (Ends.Logical.Operator == LogicalOperator.Or)
                return dates.Min();

            return null;
        }

        if (Ends?.Event != null)
        {
            var evt = instance.Events.GetValueOrDefault(Ends.Event.Id);
            // Only consider the event if it's active (not suppressed)
            if (evt?.Date != null && EventSuppressionHelper.IsEventActive(Ends.Event.Id, instance, workflowDef))
                return evt.Date;
            return null;
        }

        if (Children.Any())
        {
            var dates = Children.Select(c => c.GetEndDate(instance, workflowDef)).ToArray();
            if (dates.Any(d => d == null))
                return null;
            return dates.Max();
        }

        return null;
    }

    public DateTime? GetDeadline(WorkflowInstance instance, ModelService modelService)
    {
        if (Condition == null) return null;
        var deadlineCondition = FindDeadlineCondition(Condition);
        if (deadlineCondition is null) return null;
        var context = ObjectContext.Create(instance, modelService);
        return deadlineCondition.Evaluate(context);

        // Recursively find deadline condition
        Deadline? FindDeadlineCondition(Condition condition)
        {
            if (condition.Deadline != null) return condition.Deadline;
            if (condition.Logical is not null)
            {
                return condition.Logical.Children.Select(FindDeadlineCondition).FirstOrDefault(d => d != null);
            }

            return null;
        }
    }

    public bool HasEnded(ObjectContext context)
    {
        if (Ends != null)
            return Ends.IsMet(context);
        if (Children.Any())
            return Children.All(c => c.HasEnded(context));
        return false;
    }

    public class StepHeaderStatusConfiguration
    {
        /// <summary>
        /// Shorthand for a status that applies when a single event is active.
        /// For arbitrary logic (AND/OR/NOT over multiple events) use <see cref="Condition"/> instead.
        /// </summary>
        public string? Event { get; set; }

        /// <summary>
        /// Condition that determines whether this status applies. Supports the full condition model
        /// (logical AND/OR, NOT, events, ...). Takes precedence over <see cref="Event"/> when set.
        /// </summary>
        public Condition? Condition { get; set; }

        public StepHeaderPillType Type { get; set; }
        public BilingualString Label { get; set; } = null!;
        public BilingualTemplate LabelTemplate => field ??= BilingualTemplate.Create(Label)!;

        /// <summary>
        /// The effective condition for this status: the explicit <see cref="Condition"/> when set,
        /// otherwise the <see cref="Event"/> shorthand normalised to an event condition.
        /// </summary>
        [YamlIgnore]
        public Condition? EffectiveCondition
            => field ??= Condition ??
                         (Event != null ? new Condition { Event = new EventCondition { Id = Event } } : null);
    }
}