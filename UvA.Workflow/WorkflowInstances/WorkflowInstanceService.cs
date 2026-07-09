using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Journaling;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.WorkflowInstances;

public record WorkflowInstanceHistory(
    InstanceJournalEntry? Journal,
    List<InstanceEventLogEntry> EventLogs);

public class WorkflowInstanceService(
    ModelService modelService,
    IWorkflowInstanceRepository repository,
    IInstanceJournalService journalService,
    IInstanceEventRepository eventRepository)
{
    /// <summary>
    /// Creates a new workflow instance
    /// </summary>
    public async Task<WorkflowInstance> Create(
        string workflowDefinition,
        User createdBy,
        CancellationToken ct,
        string? userProperty = null,
        string? parentId = null,
        Dictionary<string, BsonValue>? initialProperties = null)
    {
        if (string.IsNullOrWhiteSpace(workflowDefinition))
            throw new ArgumentException("WorkflowDefinition is required", nameof(workflowDefinition));

        var instance = new WorkflowInstance
        {
            WorkflowDefinition = workflowDefinition,
            ParentId = parentId,
            CreatedOn = DateTime.Now,
            Properties = initialProperties ?? new Dictionary<string, BsonValue>(),
            Events = new Dictionary<string, InstanceEvent>()
        };

        // userProperty (e.g. Student) records who an instance is "for". For self-service
        // creation that's the creator, so default it to them. But a caller can also create
        // on someone else's behalf e.g. the API provisioning a Project for a student, and
        // supplies that user in the initial properties; in that case keep what was sent.
        if (userProperty != null && !instance.Properties.ContainsKey(userProperty))
        {
            var user = InstanceUser.FromUser(createdBy).ToBsonDocument();
            var property = modelService.WorkflowDefinitions[workflowDefinition].Properties.Get(userProperty);
            instance.Properties[userProperty] = property.IsArray ? new BsonArray { user } : user;
        }

        await repository.Create(instance, ct);
        return instance;
    }

    /// <summary>
    /// Retrieves a specific version of a workflow instance by replaying property changes
    /// from the audit journal up to the specified version thereby returning a snapshot of the workflow instance at that point in time.
    /// </summary>
    /// <param name="instanceId">The unique identifier of the workflow instance.</param>
    /// <param name="version">The version number of the workflow instance to retrieve.</param>
    /// <param name="ct">A cancellation token to observe while awaiting the task.</param>
    /// <returns>The workflow instance at the specified version.</returns>
    /// <exception cref="EntityNotFoundException">Thrown if the workflow instance with the given ID is not found.</exception>
    public async Task<WorkflowInstance> GetAsOfVersion(string instanceId, int version, CancellationToken ct)
    {
        var workflowInstance = await repository.GetById(instanceId, ct);
        if (workflowInstance == null)
            throw new EntityNotFoundException(nameof(WorkflowInstance), instanceId);

        var journal = await journalService.GetInstanceJournal(instanceId, false, ct);
        EnrichInstanceByJournalEntries(workflowInstance, journal, version);

        return workflowInstance;
    }

    public WorkflowInstance GetAsOfTimestamp(
        WorkflowInstance instance,
        DateTime timestamp,
        WorkflowInstanceHistory history)
    {
        var instanceAtTimestamp = CloneInstance(instance);
        var version = GetVersionAtTimestamp(history.Journal, timestamp);

        EnrichInstanceByJournalEntries(instanceAtTimestamp, history.Journal, version);

        instanceAtTimestamp.Events = RebuildEventsUntil(history.EventLogs, timestamp);

        RecalculateCurrentStep(instanceAtTimestamp);

        return instanceAtTimestamp;
    }

    public async Task<WorkflowInstanceHistory> GetInstanceHistory(string instanceId, CancellationToken ct)
    {
        var journal = await journalService.GetInstanceJournal(instanceId, false, ct);
        var eventLogs = await eventRepository.GetEventLogEntriesForInstance(instanceId, ct) ?? [];

        return new WorkflowInstanceHistory(journal, eventLogs);
    }

    private static void EnrichInstanceByJournalEntries(
        WorkflowInstance instance,
        InstanceJournalEntry? journal,
        int version)
    {
        if (journal == null)
            return;

        // Revert all changes after the target version against the current instance state.
        foreach (var change in journal.PropertyChanges
                     .OrderByDescending(p => p.Timestamp)
                     .Where(p => p.Version > version))
        {
            instance.SetProperty(change.OldValue, change.Path.Split('.'));
        }
    }

    private static int GetVersionAtTimestamp(InstanceJournalEntry? journal, DateTime timestamp)
    {
        if (journal is not { PropertyChanges.Length: > 0 })
            return 0;

        var changesBeforeTimestamp = journal.PropertyChanges
            .Where(pc => pc.Timestamp < timestamp)
            .ToList();

        return changesBeforeTimestamp.Any()
            ? changesBeforeTimestamp.Max(pc => pc.Version)
            : 0;
    }

    private static WorkflowInstance CloneInstance(WorkflowInstance instance)
        => BsonSerializer.Deserialize<WorkflowInstance>(instance.ToBsonDocument());

    private static Dictionary<string, InstanceEvent> RebuildEventsUntil(
        IEnumerable<InstanceEventLogEntry> eventLogs,
        DateTime timestamp)
    {
        var events = new Dictionary<string, InstanceEvent>();

        foreach (var logEntry in eventLogs
                     .Where(e => e.Timestamp <= timestamp)
                     .OrderBy(e => e.Timestamp))
        {
            if (logEntry.Operation == EventLogOperation.Delete)
            {
                events.Remove(logEntry.EventId);
                continue;
            }

            events[logEntry.EventId] = new InstanceEvent
            {
                Id = logEntry.EventId,
                Date = logEntry.EventDate
            };
        }

        return events;
    }

    private void RecalculateCurrentStep(WorkflowInstance instance)
    {
        var workflowDefinition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var context = modelService.CreateContext(instance);

        instance.CurrentStep = workflowDefinition.FlattenedSteps
            .FirstOrDefault(step => step.Condition.IsMet(context) && !step.HasEnded(context))
            ?.Name;
    }
}