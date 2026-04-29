using System.Text.RegularExpressions;

namespace UvA.Workflow.Organisations;

/// <summary>
/// MongoDB implementation of <see cref="IOrganisationRepository"/>.
/// </summary>
public class OrganisationRepository(IMongoDatabase database) : IOrganisationRepository
{
    private readonly IMongoCollection<Organisation> _collection = database.GetCollection<Organisation>("organisations");

    public async Task Create(Organisation organisation, CancellationToken ct)
    {
        await _collection.InsertOneAsync(organisation, cancellationToken: ct);
    }

    public async Task<Organisation?> GetById(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out var objectId))
            return null;

        var filter = Builders<Organisation>.Filter.Eq("_id", objectId);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<IEnumerable<Organisation>> GetAll(CancellationToken ct)
    {
        return await _collection.Find(Builders<Organisation>.Filter.Empty)
            .SortBy(o => o.Name)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Organisation>> Search(string query, int limit, CancellationToken ct)
    {
        var trimmedQuery = query.Trim();
        var regex = new BsonRegularExpression(Regex.Escape(trimmedQuery), "i");
        var filter = string.IsNullOrWhiteSpace(trimmedQuery)
            ? Builders<Organisation>.Filter.Empty
            : Builders<Organisation>.Filter.Regex(o => o.Name, regex);

        return await _collection.Find(filter)
            .SortBy(o => o.Name)
            .Limit(limit)
            .ToListAsync(ct);
    }
}