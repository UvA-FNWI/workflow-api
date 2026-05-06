using Microsoft.Extensions.Caching.Memory;

namespace UvA.Workflow.Organisations;

public class OrganisationService(IOrganisationRepository organisationRepository, IMemoryCache memoryCache)
    : IOrganisationService
{
    private static TimeSpan OrganisationCacheExpiration => TimeSpan.FromMinutes(15);
    private static string GetCacheKey(string organisationId) => $"organisation:{organisationId}";

    public async Task<Organisation> CreateOrganisation(string name, CancellationToken ct = default)
    {
        var organisation = new Organisation
        {
            Name = name
        };

        await organisationRepository.Create(organisation, ct);
        memoryCache.Set(GetCacheKey(organisation.Id), organisation, OrganisationCacheExpiration);
        return organisation;
    }

    public async Task<Organisation?> GetOrganisation(string id, CancellationToken ct)
    {
        var cacheKey = GetCacheKey(id);
        if (memoryCache.TryGetValue(cacheKey, out Organisation? organisation))
            return organisation;

        organisation = await organisationRepository.GetById(id, ct);
        if (organisation != null)
            memoryCache.Set(cacheKey, organisation, OrganisationCacheExpiration);
        return organisation;
    }

    public Task<IEnumerable<Organisation>> GetAll(CancellationToken ct)
        => organisationRepository.GetAll(ct);

    public Task<IEnumerable<Organisation>> Search(string query, int limit, CancellationToken ct)
        => organisationRepository.Search(query, limit, ct);
}