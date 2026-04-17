using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Persistence;

public interface ISettingsStore
{
    Task<string?> Get(string key, CancellationToken ct = default);
    Task Set(string key, string value, CancellationToken ct = default);
}