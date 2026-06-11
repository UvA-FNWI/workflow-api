using Microsoft.Extensions.Caching.Memory;

namespace UvA.Workflow.Organizations;

public class OrganizationService(IOrganizationRepository organizationRepository)
    : IOrganizationService
{
    public async Task<Organization> CreateOrganization(string name, CancellationToken ct = default)
    {
        var organization = await organizationRepository.GetByName(name, ct);
        if (organization != null)
            return organization;

        organization = new Organization
        {
            Name = name
        };

        await organizationRepository.Create(organization, ct);

        return organization;
    }

    public Task<Organization?> GetOrganization(string id, CancellationToken ct)
        => organizationRepository.GetById(id, ct);

    public Task<Organization?> GetOrganizationByName(string name, CancellationToken ct)
        => organizationRepository.GetByName(name, ct);

    public Task<IEnumerable<Organization>> GetAll(CancellationToken ct)
        => organizationRepository.GetAll(ct);

    public Task<IEnumerable<Organization>> Search(string query, int limit, CancellationToken ct)
        => organizationRepository.Search(query, limit, ct);
}