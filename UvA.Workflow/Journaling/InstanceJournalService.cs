using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;

namespace UvA.Workflow.Journaling;

public interface IInstanceJournalService
{
    Task<InstanceJournalEntry?> GetInstanceJournal(string instanceId, bool createIfNotExist = false,
        CancellationToken ct = default);

    Task LogPropertyChange(string instanceId, PropertyChangeEntry valueChange, CancellationToken ct = default);

    Task LogPropertyChanges(string instanceId, ICollection<PropertyChangeEntry> newChanges,
        CancellationToken ct = default);

    Task<int> IncrementVersion(string instanceId, CancellationToken ct = default);
}