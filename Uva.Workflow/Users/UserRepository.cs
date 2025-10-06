using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Uva.Workflow.Users;

/// <summary>
/// MongoDB implementation of the IUserRepository contract.
/// Handles mapping between domain entities and MongoDB documents.
/// </summary>
public class UserRepository(IMongoDatabase database) : IUserRepository
{
    private readonly IMongoCollection<UserDocument> _collection = database.GetCollection<UserDocument>("users");

    public async Task CreateAsync(User user)
    {
        var document = MapToDocument(user);
        await _collection.InsertOneAsync(document);
        user.Id = document.Id.ToString(); // Update with generated ID
    }

    public async Task<User?> GetByIdAsync(string id)
    {
        if (!ObjectId.TryParse(id, out var objectId))
            return null;

        var filter = Builders<UserDocument>.Filter.Eq("_id", objectId);
        var document = await _collection.Find(filter).FirstOrDefaultAsync();
        return document != null ? MapToDomain(document) : null;
    }

    public async Task UpdateAsync(User user)
    {
        if (!ObjectId.TryParse(user.Id, out var objectId))
            throw new ArgumentException("Invalid user ID", nameof(user.Id));

        var document = MapToDocument(user);
        document.Id = objectId;

        var filter = Builders<UserDocument>.Filter.Eq("_id", objectId);
        await _collection.ReplaceOneAsync(filter, document);
    }

    public async Task<User?> GetByExternalIdAsync(string externalId)
    {
        var filter = Builders<UserDocument>.Filter.Eq(x => x.ExternalId, externalId);
        var document = await _collection.Find(filter).FirstOrDefaultAsync();
        return document != null ? MapToDomain(document) : null;
    }

    public async Task<IEnumerable<User>> GetByIdsAsync(IReadOnlyList<string> ids)
    {
        var objectIds = ids
            .Select(id => ObjectId.TryParse(id, out var oid) ? oid : (ObjectId?)null)
            .Where(oid => oid.HasValue)
            .Select(oid => oid!.Value)
            .ToList();

        var filter = Builders<UserDocument>.Filter.In("_id", objectIds);
        var documents = await _collection.Find(filter).ToListAsync();
        return documents.Select(MapToDomain);
    }

    // Mapping methods
    private static UserDocument MapToDocument(User domain)
    {
        return new UserDocument
        {
            Id = string.IsNullOrEmpty(domain.Id) ? ObjectId.Empty : ObjectId.Parse(domain.Id),
            ExternalId = domain.ExternalId,
            DisplayName = domain.DisplayName,
            Email = domain.Email
        };
    }

    private static User MapToDomain(UserDocument document)
    {
        return new User
        {
            Id = document.Id.ToString(),
            ExternalId = document.ExternalId,
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
    
    public string ExternalId { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Email { get; set; } = null!;
}

