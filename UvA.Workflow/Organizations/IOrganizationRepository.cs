namespace UvA.Workflow.Organizations;

/// <summary>
/// Repository contract for Organization persistence.
/// </summary>
public interface IOrganizationRepository
{
    Task Create(Organization organization, CancellationToken ct);
    Task<Organization?> GetById(string id, CancellationToken ct);
    Task<IEnumerable<Organization>> GetAll(CancellationToken ct);
    Task<IEnumerable<Organization>> Search(string query, int limit, CancellationToken ct);
}