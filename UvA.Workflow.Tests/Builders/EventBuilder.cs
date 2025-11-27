using UvA.Workflow.Events;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests;

public class EventBuilder
{
    private string? id;
    private DateTime? date;

    public EventBuilder WithId(string id)
    {
        this.id = id;
        return this;
    }

    public EventBuilder AsCompleted(DateTime completionDate)
    {
        date = completionDate;
        return this;
    }

    /// <summary>
    /// Marks the event as completed by setting the specified completion date and returns the updated <see cref="EventBuilder"/> instance.
    /// </summary>
    /// <param name="completionDateString">The date and time when the event was completed.</param>
    /// <returns>The updated <see cref="EventBuilder"/> instance with the completion date set.</returns>
    public EventBuilder AsCompleted(string completionDateString)
    {
        date = DateTime.SpecifyKind(DateTime.Parse(completionDateString), DateTimeKind.Utc);
        return this;
    }


    /// <summary>
    /// Marks the event as completed by setting the completion date to the specified number of days ago and returns the updated <see cref="EventBuilder"/> instance.
    /// </summary>
    /// <param name="daysAgo">The number of days before today to set the completion date. Defaults to 1.</param>
    /// <returns>The updated <see cref="EventBuilder"/> instance with the completion date set.</returns>
    public EventBuilder AsCompleted(int daysAgo = 1)
    {
        date = DateTime.Now.AddDays(daysAgo);
        return this;
    }

    public InstanceEvent Build() => new InstanceEvent
    {
        Id = id ?? throw new InvalidOperationException("Id must be specified"),
        Date = date
    };
}