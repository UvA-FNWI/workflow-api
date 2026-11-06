using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Identity.Client;

namespace UvA.Workflow.Users.EntraId.Infrastructure;

internal class EntraGraphService(
    IConfidentialClientApplication confidentialClientApplication,
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    ILogger<EntraGraphService> logger) : IEntraGraphService
{
    private static readonly string[] AppOnlyScopes = ["https://graph.microsoft.com/.default"];
    private static readonly TimeSpan TokenRefreshBuffer = TimeSpan.FromMinutes(5);

    private const string UserSelect =
        "displayName,userPrincipalName,mail,onPremisesSamAccountName,mailNickname";

    private const string TokenCacheKey = "entra:token:app-only";

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("EntraGraph");

    public async Task<IReadOnlyList<EntraUserInfo>> SearchUsersAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var term = query.Trim().Replace("\"", string.Empty, StringComparison.Ordinal);
        if (term.Length < 2)
            return [];

        var token = await GetAccessTokenAsync(ct);
        var search = Uri.EscapeDataString(
            $"\"displayName:{term}\" OR \"mail:{term}\" OR \"userPrincipalName:{term}\"");
        var url =
            $"https://graph.microsoft.com/v1.0/users?$search={search}&$select={UserSelect}&$count=true&$top=25";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("ConsistencyLevel", "eventual");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("Failed to search Entra users - HTTP {Status} - {Error}",
                    response.StatusCode, errorContent);
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("value", out var valueElement))
                return [];

            return valueElement.EnumerateArray()
                .Select(TryReadUser)
                .OfType<EntraUserInfo>()
                .ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Exception while searching Entra users for '{Query}'", term);
            return [];
        }
    }

    public async Task<EntraUserInfo?> GetUserByUpnAsync(string upn, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(upn))
            return null;

        var token = await GetAccessTokenAsync(ct);
        var escapedUpn = upn.Replace("'", "''", StringComparison.Ordinal);
        var filter = Uri.EscapeDataString($"userPrincipalName eq '{escapedUpn}'");
        var url = $"https://graph.microsoft.com/v1.0/users?$filter={filter}&$select={UserSelect}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Failed to read Entra user '{Upn}' - HTTP {Status} - {Error}",
                upn, response.StatusCode, errorContent);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("value", out var valueElement))
            return null;

        return valueElement.EnumerateArray().Select(TryReadUser).OfType<EntraUserInfo>().FirstOrDefault();
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (cache.TryGetValue<(string Token, DateTimeOffset ExpiresOn)>(TokenCacheKey, out var cached)
            && cached.ExpiresOn > DateTimeOffset.UtcNow.Add(TokenRefreshBuffer))
            return cached.Token;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue<(string Token, DateTimeOffset ExpiresOn)>(TokenCacheKey, out cached)
                && cached.ExpiresOn > DateTimeOffset.UtcNow.Add(TokenRefreshBuffer))
                return cached.Token;

            var result = await confidentialClientApplication.AcquireTokenForClient(AppOnlyScopes).ExecuteAsync(ct);
            cache.Set(TokenCacheKey, (result.AccessToken, result.ExpiresOn),
                result.ExpiresOn - DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1));
            return result.AccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static EntraUserInfo? TryReadUser(JsonElement user)
    {
        var upn = user.TryGetProperty("userPrincipalName", out var upnElement) ? upnElement.GetString() : null;
        var displayName = user.TryGetProperty("displayName", out var displayNameElement)
            ? displayNameElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(upn) || string.IsNullOrWhiteSpace(displayName))
            return null;

        var mail = user.TryGetProperty("mail", out var mailElement) ? mailElement.GetString() : null;
        var sam = user.TryGetProperty("onPremisesSamAccountName", out var samElement) ? samElement.GetString() : null;
        var nickname = user.TryGetProperty("mailNickname", out var nicknameElement)
            ? nicknameElement.GetString()
            : null;

        return new EntraUserInfo(
            upn,
            displayName,
            string.IsNullOrWhiteSpace(mail) ? upn : mail,
            string.IsNullOrWhiteSpace(sam) ? null : sam,
            string.IsNullOrWhiteSpace(nickname) ? null : nickname);
    }
}