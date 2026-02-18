using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Persistence;

public interface ISettingsStore
{
    Task<string?> Get(string key, CancellationToken ct = default);
    Task Set(string key, string value, CancellationToken ct = default);
}

public class SettingsStore(IMongoDatabase database) : ISettingsStore
{
    private readonly IMongoCollection<SettingsEntry> _settingsCollection =
        database.GetCollection<SettingsEntry>("settings");

    public async Task<string?> Get(string key, CancellationToken ct = default)
    {
        var setting = await _settingsCollection.Find(s => s.Key == key).FirstOrDefaultAsync(ct);
        return setting?.Value;
    }

    public async Task Set(string key, string value, CancellationToken ct = default)
    {
        var update = Builders<SettingsEntry>.Update
            .Set(s => s.Value, value)
            .Set(s => s.UpdatedAt, DateTime.UtcNow);

        await _settingsCollection.UpdateOneAsync(
            s => s.Key == key,
            update,
            new UpdateOptions { IsUpsert = true },
            ct);
    }
}

public class SettingsEntry
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    public string Key { get; set; } = null!;
    public string Value { get; set; } = null!;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}