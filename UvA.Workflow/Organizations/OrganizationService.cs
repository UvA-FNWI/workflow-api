using Microsoft.Extensions.Caching.Memory;

namespace UvA.Workflow.Organizations;

public class OrganizationService(IOrganizationRepository organizationRepository, IMemoryCache memoryCache)
    : IOrganizationService
{
    private static TimeSpan OrganizationCacheExpiration => TimeSpan.FromMinutes(15);
    private static string GetCacheKey(string organizationId) => $"organization:{organizationId}";

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
        memoryCache.Set(GetCacheKey(organization.Id), organization, OrganizationCacheExpiration);

        return organization;
    }

    public async Task<Organization?> GetOrganization(string id, CancellationToken ct)
    {
        var cacheKey = GetCacheKey(id);
        if (memoryCache.TryGetValue(cacheKey, out Organization? organization))
            return organization;

        organization = await organizationRepository.GetById(id, ct);
        if (organization != null)
            memoryCache.Set(cacheKey, organization, OrganizationCacheExpiration);
        return organization;
    }

    public Task<Organization?> GetOrganizationByName(string name, CancellationToken ct)
        => organizationRepository.GetByName(name, ct);

    public Task<IEnumerable<Organization>> GetAll(CancellationToken ct)
        => organizationRepository.GetAll(ct);

    public Task<IEnumerable<Organization>> Search(string query, int limit, CancellationToken ct)
        => organizationRepository.Search(query, limit, ct);
}