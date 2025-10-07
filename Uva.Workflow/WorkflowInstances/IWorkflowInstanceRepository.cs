using System.Linq.Expressions;

namespace Uva.Workflow.WorkflowInstances;

/// <summary>
/// Repository contract for WorkflowInstance persistence.
/// Defines the interface without implementation details.
/// </summary>
public interface IWorkflowInstanceRepository
{
    // Core CRUD
    Task CreateAsync(WorkflowInstance instance);
    Task<WorkflowInstance?> GetByIdAsync(string id);
    Task UpdateAsync(WorkflowInstance instance);
    Task DeleteAsync(string id);

    // Query operations
    Task<IEnumerable<WorkflowInstance>> GetByIdsAsync(IEnumerable<string> ids);
    Task<IEnumerable<WorkflowInstance>> GetByEntityTypeAsync(string entityType);
    Task<IEnumerable<WorkflowInstance>> GetByParentIdAsync(string parentId);

    // Advanced query methods
    Task<List<WorkflowInstance>> GetAllAsync(Expression<Func<WorkflowInstance, bool>> expression);
    Task<T?> GetAsync<T>(string instanceId, Expression<Func<WorkflowInstance, T>> expression);

    Task<T?> GetAsync<T>(Expression<Func<WorkflowInstance, bool>> predicate,
        Expression<Func<WorkflowInstance, T>> project);

    Task<List<Dictionary<string, BsonValue>>> GetAllByTypeAsync(string entityType,
        Dictionary<string, string> projection);

    Task<List<Dictionary<string, BsonValue>>> GetAllByParentIdAsync(string parentId,
        Dictionary<string, string> projection);

    Task<List<Dictionary<string, BsonValue>>> GetAllByIdAsync(string[] ids, Dictionary<string, string> projection);

    // Type-safe update methods
    Task UpdateFieldAsync<TField>(string instanceId, Expression<Func<WorkflowInstance, TField>> field, TField value);
    Task UpdateFieldsAsync(string instanceId, UpdateDefinition<WorkflowInstance> updateDefinition);
}