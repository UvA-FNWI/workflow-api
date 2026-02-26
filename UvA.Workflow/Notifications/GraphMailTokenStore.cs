using System.Text;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Persistence;

namespace UvA.Workflow.Notifications;

public interface IGraphMailTokenStore
{
    Task<byte[]> GetTokenCache(CancellationToken ct = default);
    Task SetTokenCache(byte[] tokenCache, CancellationToken ct = default);
}

public class GraphMailTokenStore(ISettingsStore settingsStore, IEncryptionService encryptionService)
    : IGraphMailTokenStore
{
    public async Task<byte[]> GetTokenCache(CancellationToken ct = default)
    {
        var encryptedTokenCache = await settingsStore.Get(GraphMailOptions.TokenSettingKey, ct);
        if (string.IsNullOrWhiteSpace(encryptedTokenCache))
            throw new InvalidOperationException("GraphMailToken not set in settings collection");
        return encryptionService.DecryptAes(Convert.FromBase64String(encryptedTokenCache));
    }

    public async Task SetTokenCache(byte[] tokenCache, CancellationToken ct = default)
    {
        var encryptedTokenCache = encryptionService.EncryptAes(tokenCache);
        await settingsStore.Set(GraphMailOptions.TokenSettingKey, Convert.ToBase64String(encryptedTokenCache), ct);
    }
}