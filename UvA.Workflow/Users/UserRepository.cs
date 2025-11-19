using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Users;

/// <summary>
/// MongoDB implementation of the IUserRepository contract.
/// Handles mapping between domain entities and MongoDB documents.
/// </summary>
public class UserRepository(IMongoDatabase database) : IUserRepository
{
    private readonly IMongoCollection<UserDocument> _collection = database.GetCollection<UserDocument>("users");

    public async Task Create(User user, CancellationToken ct)
    {
        var document = MapToDocument(user);
        await _collection.InsertOneAsync(document, cancellationToken: ct);
        user.Id = document.Id.ToString(); // Update with generated ID
    }

    public async Task<User?> GetById(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out var objectId))
            return null;

        var filter = Builders<UserDocument>.Filter.Eq("_id", objectId);
        var document = await _collection.Find(filter).FirstOrDefaultAsync(ct);
        return document != null ? MapToDomain(document) : null;
    }

    public async Task Update(User user, CancellationToken ct)
    {
        if (!ObjectId.TryParse(user.Id, out var objectId))
            throw new ArgumentException("Invalid user ID", nameof(user.Id));

        var document = MapToDocument(user);
        document.Id = objectId;

        var filter = Builders<UserDocument>.Filter.Eq("_id", objectId);
        await _collection.ReplaceOneAsync(filter, document, cancellationToken: ct);
    }

    public async Task<User?> GetByExternalId(string externalId, CancellationToken ct)
    {
        var filter = Builders<UserDocument>.Filter.Eq(x => x.UserName, externalId);
        var document = await _collection.Find(filter).FirstOrDefaultAsync(ct);
        return document != null ? MapToDomain(document) : null;
    }

    public async Task<IEnumerable<User>> GetByIds(IReadOnlyList<string> ids, CancellationToken ct)
    {
        var objectIds = ids
            .Select(id => ObjectId.TryParse(id, out var oid) ? oid : (ObjectId?)null)
            .Where(oid => oid.HasValue)
            .Select(oid => oid!.Value)
            .ToList();

        var filter = Builders<UserDocument>.Filter.In("_id", objectIds);
        var documents = await _collection.Find(filter).ToListAsync(ct);
        return documents.Select(MapToDomain);
    }

    // Mapping methods
    private static UserDocument MapToDocument(User domain)
    {
        return new UserDocument
        {
            Id = string.IsNullOrEmpty(domain.Id) ? ObjectId.Empty : ObjectId.Parse(domain.Id),
            UserName = domain.UserName,
            DisplayName = domain.DisplayName,
            Email = domain.Email
        };
    }

    private static User MapToDomain(UserDocument document)
    {
        return new User
        {
            Id = document.Id.ToString(),
            UserName = document.UserName,
            DisplayName = document.DisplayName,
            Email = document.Email
        };
    }
}

// MongoDB document model
internal class UserDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public ObjectId Id { get; set; }

    //TODO: Consider using a different field name for external ID
    [BsonElement("ExternalId")] public string UserName { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Email { get; set; } = null!;
}