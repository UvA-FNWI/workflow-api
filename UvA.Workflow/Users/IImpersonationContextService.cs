namespace UvA.Workflow.Users;

/// <summary>
/// Resolves the effective impersonated role for the current request and workflow instance.
/// </summary>
public interface IImpersonationContextService
{
    Task<string?> GetImpersonatedRole(WorkflowInstance instance, CancellationToken ct = default);
}

/// <summary>
/// Default no-op implementation used outside API request contexts.
/// </summary>
public class NoImpersonationContextService : IImpersonationContextService
{
    public Task<string?> GetImpersonatedRole(WorkflowInstance instance, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}