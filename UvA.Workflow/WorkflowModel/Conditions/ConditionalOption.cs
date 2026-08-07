namespace UvA.Workflow.WorkflowModel.Conditions;

public abstract class ConditionalOption
{
    /// <summary>
    /// Shorthand for a condition that applies when a single event is active.
    /// For arbitrary logic use <see cref="Condition"/> instead.
    /// </summary>
    public string? Event { get; set; }

    /// <summary>
    /// Condition that determines whether this configuration applies. Takes precedence over
    /// <see cref="Event"/> when set. Omit both properties for an unconditional configuration.
    /// </summary>
    public Condition? Condition { get; set; }

    [YamlIgnore]
    public Condition? EffectiveCondition
        => Condition ??
           (Event != null ? new Condition { Event = new EventCondition { Id = Event } } : null);
}