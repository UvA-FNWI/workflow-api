using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;

namespace UvA.Workflow.Journaling;

public interface IInstanceJournalService
{
    Task<InstanceJournalEntry?> GetInstanceJournal(string instanceId, bool createIfNotExist = false,
        CancellationToken ct = default);

    /// <summary>
    /// Logs the change of a property. Returns true if the existing journal entry has been replaced,
    /// false if a new one has been created.
    /// </summary>
    Task<bool> LogPropertyChange(string instanceId, PropertyChangeEntry valueChange, CancellationToken ct = default);

    Task<int> IncrementVersion(string instanceId, CancellationToken ct = default);
}