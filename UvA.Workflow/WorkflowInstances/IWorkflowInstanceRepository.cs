using System.Linq.Expressions;

namespace UvA.Workflow.WorkflowInstances;

/// <summary>
/// Repository contract for WorkflowInstance persistence.
/// Defines the interface without implementation details.
/// </summary>
public interface IWorkflowInstanceRepository
{
    // Core CRUD
    Task Create(WorkflowInstance instance, CancellationToken ct);
    Task<WorkflowInstance?> GetById(string id, CancellationToken ct);
    Task Update(WorkflowInstance instance, CancellationToken ct);
    Task Delete(string id, CancellationToken ct);

    // Query operations
    Task<IEnumerable<WorkflowInstance>> GetByIds(IEnumerable<string> ids, CancellationToken ct);
    Task<IEnumerable<WorkflowInstance>> GetByEntityType(string entityType, CancellationToken ct);
    Task<IEnumerable<WorkflowInstance>> GetByParentId(string parentId, CancellationToken ct);

    // Advanced query methods
    Task<List<WorkflowInstance>> GetAll(Expression<Func<WorkflowInstance, bool>> expression, CancellationToken ct);
    Task<T?> Get<T>(string instanceId, Expression<Func<WorkflowInstance, T>> expression, CancellationToken ct);

    Task<T?> Get<T>(Expression<Func<WorkflowInstance, bool>> predicate,
        Expression<Func<WorkflowInstance, T>> project, CancellationToken ct);

    Task<List<Dictionary<string, BsonValue>>> GetAllByType(string entityType,
        Dictionary<string, string> projection, CancellationToken ct);

    Task<List<Dictionary<string, BsonValue>>> GetAllByParentId(string parentId,
        Dictionary<string, string> projection, CancellationToken ct);

    Task<List<Dictionary<string, BsonValue>>> GetAllById(string[] ids, Dictionary<string, string> projection,
        CancellationToken ct);

    // Type-safe update methods
    Task UpdateField<TField>(string instanceId, Expression<Func<WorkflowInstance, TField>> field, TField value,
        CancellationToken ct);

    Task UpdateFields(string instanceId, UpdateDefinition<WorkflowInstance> updateDefinition, CancellationToken ct);
    Task DeleteField(string instanceId, Expression<Func<WorkflowInstance, object>> field, CancellationToken ct);
    Task AddOrUpdateEvent(WorkflowInstance instance, InstanceEvent newEvent, User user, CancellationToken ct);

    /// <summary>
    /// Deletes a specified event from a workflow instance and logs the deletion.
    /// </summary>
    /// <param name="instance">The workflow instance from which the event is to be deleted.</param>
    /// <param name="eventToDelete">The event to remove from the instance.</param>
    /// <param name="user">The user executing the deletion action.</param>
    /// <param name="ct">The cancellation token used to observe the operation's cancellation.</param>
    /// <returns>An asynchronous operation representing the deletion process.</returns>
    Task DeleteEvent(WorkflowInstance instance, InstanceEvent eventToDelete, User user, CancellationToken ct);
}