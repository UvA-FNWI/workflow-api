using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace UvA.Workflow.Persistence.Mongo;

/// <summary>
/// MongoDB implementation of the IUserRepository contract.
/// Handles mapping between domain entities and MongoDB documents.
/// </summary>
public class UserRepository(IMongoDatabase database) : IUserRepository
{
    private readonly IMongoCollection<User> _collection = database.GetCollection<User>("users");

    public async Task Create(User user, CancellationToken ct)
    {
        user.SearchTerms = UserSearchTerms.Create(user);
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
        if (!ObjectId.TryParse(user.Id, out var objectId))
            throw new ArgumentException("Invalid user ID", nameof(user.Id));

        user.SearchTerms = UserSearchTerms.Create(user);
        var filter = Builders<User>.Filter.Eq("_id", objectId);
        var result = await _collection.ReplaceOneAsync(filter, user, cancellationToken: ct);

        if (result.IsAcknowledged && result.MatchedCount == 0)
            throw new InvalidOperationException($"User '{user.Id}' was not found for update.");
    }

    public async Task<User?> GetByExternalId(string externalId, CancellationToken ct)
    {
        var filter = Builders<User>.Filter.Eq(x => x.UserName, externalId);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<IEnumerable<User>> GetByUserNames(IReadOnlyCollection<string> userNames, CancellationToken ct)
    {
        var filter = Builders<User>.Filter.In(x => x.UserName, userNames);
        return await _collection.Find(filter).ToListAsync(ct);
    }

    public async Task<User?> GetByEmail(string email, CancellationToken ct)
    {
        var filter = Builders<User>.Filter.Regex(
            x => x.Email,
            new BsonRegularExpression($"^{Regex.Escape(email.Trim())}$", "i"));
        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<User?> GetByEmailAndProvider(string email, string providerKey, CancellationToken ct)
    {
        var filter = Builders<User>.Filter.And(
            Builders<User>.Filter.Regex(
                x => x.Email,
                new BsonRegularExpression($"^{Regex.Escape(email.Trim())}$", "i")),
            Builders<User>.Filter.Eq(x => x.ProviderKey, providerKey));

        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<IEnumerable<User>> SearchByQuery(string query, CancellationToken ct)
        => await SearchByQuery(query, null, ct);

    public async Task<IEnumerable<User>> SearchByQueryAndProvider(string query, string providerKey,
        CancellationToken ct)
        => await SearchByQuery(query, providerKey, ct);

    public async IAsyncEnumerable<User> SearchByPrefix(string query, [EnumeratorCancellation] CancellationToken ct)
    {
        var normalized = UserSearchTerms.Normalize(query);
        if (normalized.Length == 0)
            yield break;

        var prefix = new BsonRegularExpression($"^{Regex.Escape(normalized)}");
        var builder = Builders<User>.Filter;
        // Older deployments can still replace a user without search terms during a rollout.
        var filter = builder.Regex("SearchTerms", prefix) | builder.Eq(user => user.SearchTerms, null);
        using var users = await _collection.Find(filter)
            .SortBy(user => user.DisplayName)
            .ThenBy(user => user.UserName)
            .ThenBy(user => user.Id)
            .ToCursorAsync(ct);
        while (await users.MoveNextAsync(ct))
        {
            foreach (var user in users.Current)
                if (user.SearchTerms != null ||
                    UserSearchTerms.Create(user).Any(term => term.StartsWith(normalized, StringComparison.Ordinal)))
                    yield return user;
        }
    }

    private async Task<IEnumerable<User>> SearchByQuery(string query, string? providerKey, CancellationToken ct)
    {
        var trimmedQuery = query.Trim();
        var filters = new List<FilterDefinition<User>>();

        if (!string.IsNullOrWhiteSpace(trimmedQuery))
        {
            var regex = new BsonRegularExpression(Regex.Escape(trimmedQuery), "i");
            filters.Add(Builders<User>.Filter.Or(
                Builders<User>.Filter.Regex(x => x.DisplayName, regex),
                Builders<User>.Filter.Regex(x => x.Email, regex),
                Builders<User>.Filter.Regex(x => x.UserName, regex)));
        }

        if (!string.IsNullOrWhiteSpace(providerKey))
            filters.Add(Builders<User>.Filter.Eq(x => x.ProviderKey, providerKey));

        var filter = filters.Count switch
        {
            0 => Builders<User>.Filter.Empty,
            1 => filters[0],
            _ => Builders<User>.Filter.And(filters)
        };

        return await _collection.Find(filter)
            .SortBy(x => x.DisplayName)
            .ThenBy(x => x.UserName)
            .ToListAsync(ct);
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