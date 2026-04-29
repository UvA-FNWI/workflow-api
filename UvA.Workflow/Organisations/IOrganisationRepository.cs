namespace UvA.Workflow.Organisations;

/// <summary>
/// Repository contract for Organisation persistence.
/// </summary>
public interface IOrganisationRepository
{
    Task Create(Organisation organisation, CancellationToken ct);
    Task<Organisation?> GetById(string id, CancellationToken ct);
    Task<IEnumerable<Organisation>> GetAll(CancellationToken ct);
    Task<IEnumerable<Organisation>> Search(string query, CancellationToken ct);
}