using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Journaling;

namespace UvA.Workflow.WorkflowInstances;

public class WorkflowInstanceService(
    ModelService modelService,
    IWorkflowInstanceRepository repository,
    IInstanceJournalService journalService)
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

        if (userProperty != null)
        {
            var user = createdBy.ToBsonDocument();
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
        if (journal != null)
        {
            // Revert all changes after the specified version
            foreach (var change in journal.PropertyChanges
                         .OrderByDescending(p => p.Timestamp)
                         .Where(p => p.Version > version))
            {
                workflowInstance.SetProperty(change.OldValue, change.Path.Split('.'));
            }
        }

        return workflowInstance;
    }

    /// <summary>
    /// Retrieves a specific snapshot of a workflow instance at the provided timestamp.
    /// </summary>
    public async Task<WorkflowInstance> GetAsOfTimestamp(string instanceId, DateTime timestamp, CancellationToken ct)
    {
        var version = await GetVersionAtTimestamp(instanceId, timestamp, ct);
        return await GetAsOfVersion(instanceId, version, ct);
    }

    /// <summary>
    /// Gets the version number that was active at a specific timestamp.
    /// </summary>
    public async Task<int> GetVersionAtTimestamp(string instanceId, DateTime timestamp, CancellationToken ct)
    {
        var journal = await journalService.GetInstanceJournal(instanceId, false, ct);

        if (journal is not { PropertyChanges.Length: > 0 })
            return 0;

        var changesBeforeTimestamp = journal.PropertyChanges
            .Where(pc => pc.Timestamp < timestamp)
            .ToList();

        return changesBeforeTimestamp.Any()
            ? changesBeforeTimestamp.Max(pc => pc.Version)
            : 0;
    }

    /// <summary>
    /// Updates multiple properties on a workflow instance in a single operation
    /// </summary>
    public async Task UpdateProperties(
        string instanceId,
        Dictionary<string, BsonValue> properties,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentException("InstanceId is required", nameof(instanceId));

        var instance = await repository.GetById(instanceId, ct);
        if (instance == null)
            throw new ArgumentException("Instance not found", nameof(instanceId));

        foreach (var (propertyPath, value) in properties)
        {
            var pathParts = propertyPath.Split('.');
            instance.SetProperty(value, pathParts);
        }

        await repository.Update(instance, ct);
    }
}