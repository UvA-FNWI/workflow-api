namespace UvA.Workflow.Organisations;

public interface IOrganisationService
{
    Task<Organisation> CreateOrganisation(string name, CancellationToken ct = default);
    Task<Organisation?> GetOrganisation(string id, CancellationToken ct);
    Task<IEnumerable<Organisation>> GetAll(CancellationToken ct);
    Task<IEnumerable<Organisation>> Search(string query, int limit, CancellationToken ct);
}