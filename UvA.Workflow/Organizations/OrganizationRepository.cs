using System.Text.RegularExpressions;

namespace UvA.Workflow.Organizations;

/// <summary>
/// MongoDB implementation of <see cref="IOrganizationRepository"/>.
/// </summary>
public class OrganizationRepository(IMongoDatabase database) : IOrganizationRepository
{
    private readonly IMongoCollection<Organization> _collection = database.GetCollection<Organization>("organisations");

    public async Task Create(Organization organization, CancellationToken ct)
    {
        await _collection.InsertOneAsync(organization, cancellationToken: ct);
    }

    public async Task<Organization?> GetById(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out var objectId))
            return null;

        var filter = Builders<Organization>.Filter.Eq("_id", objectId);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<IEnumerable<Organization>> GetAll(CancellationToken ct)
    {
        return await _collection.Find(Builders<Organization>.Filter.Empty)
            .SortBy(o => o.Name)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Organization>> Search(string query, int limit, CancellationToken ct)
    {
        var trimmedQuery = query.Trim();
        var regex = new BsonRegularExpression(Regex.Escape(trimmedQuery), "i");
        var filter = string.IsNullOrWhiteSpace(trimmedQuery)
            ? Builders<Organization>.Filter.Empty
            : Builders<Organization>.Filter.Regex(o => o.Name, regex);

        return await _collection.Find(filter)
            .SortBy(o => o.Name)
            .Limit(limit)
            .ToListAsync(ct);
    }
}