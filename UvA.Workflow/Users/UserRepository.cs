namespace UvA.Workflow.Users;

/// <summary>
/// MongoDB implementation of the IUserRepository contract.
/// Handles mapping between domain entities and MongoDB documents.
/// </summary>
public class UserRepository(IMongoDatabase database) : IUserRepository
{
    private readonly IMongoCollection<User> _collection = database.GetCollection<User>("users");

    public async Task Create(User user, CancellationToken ct)
    {
        await _collection.InsertOneAsync(user, cancellationToken: ct);
    }

    public async Task<User?> GetById(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out var objectId))
            return null;

        var filter = Builders<User>.Filter.Eq("_id", objectId);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task Update(User user, CancellationToken ct)
    {
        var filter = Builders<User>.Filter.Eq("_id", user.Id);
        await _collection.ReplaceOneAsync(filter, user, cancellationToken: ct);
    }

    public async Task<User?> GetByExternalId(string externalId, CancellationToken ct)
    {
        var filter = Builders<User>.Filter.Eq(x => x.UserName, externalId);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<IEnumerable<User>> GetByIds(IReadOnlyList<string> ids, CancellationToken ct)
    {
        var objectIds = ids
            .Select(id => ObjectId.TryParse(id, out var oid) ? oid : (ObjectId?)null)
            .Where(oid => oid.HasValue)
            .Select(oid => oid!.Value)
            .ToList();

        var filter = Builders<User>.Filter.In("_id", objectIds);
        return await _collection.Find(filter).ToListAsync(ct);
    }
}