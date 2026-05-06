namespace UvA.Workflow.Organizations;

public interface IOrganizationService
{
    Task<Organization> CreateOrganization(string name, CancellationToken ct = default);
    Task<Organization?> GetOrganization(string id, CancellationToken ct);
    Task<IEnumerable<Organization>> GetAll(CancellationToken ct);
    Task<IEnumerable<Organization>> Search(string query, int limit, CancellationToken ct);
}