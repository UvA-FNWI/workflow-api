using System.Net.Http.Headers;
using System.Text.Json;

namespace UvA.Workflow.Users.EntraId.Infrastructure;

internal class EntraGraphService(
    IEntraAccessTokenSource tokens,
    IHttpClientFactory httpClientFactory,
    ILogger<EntraGraphService> logger) : IEntraGraphService
{
    private const string UserSelect =
        "displayName,userPrincipalName,mail,accountEnabled,userType,onPremisesSamAccountName,onPremisesExtensionAttributes";

    public const int MaxResults = 10;

    /// <summary>
    /// Medewerkers only: a Member, the account is enabled, extensionAttribute2 is "valid", and
    /// extensionAttribute4 (eduPersonPrimaryAffiliation) is "employee". Guests are Members of
    /// the other tenant and also carry extensionAttribute4 "employee", so userType is what
    /// keeps them out. Their UPN contains "#EXT#", which Graph will not filter on.
    /// </summary>
    public const string EligibleUserFilter =
        "accountEnabled eq true and userType eq 'Member' and onPremisesExtensionAttributes/extensionAttribute2 eq 'valid' and onPremisesExtensionAttributes/extensionAttribute4 eq 'employee'";

    public async Task<IReadOnlyList<EntraUserInfo>> SearchUsersAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var term = query.Trim().Replace("\"", string.Empty, StringComparison.Ordinal);
        if (term.Length < 2)
            return [];

        var search = Uri.EscapeDataString(
            $"\"displayName:{term}\" OR \"mail:{term}\" OR \"userPrincipalName:{term}\"");
        var eligible = Uri.EscapeDataString(EligibleUserFilter);
        var searchUrl =
            $"https://graph.microsoft.com/v1.0/users?$search={search}&$filter={eligible}&$select={UserSelect}&$count=true&$top={MaxResults}";

        // A SAM prefix search is a second query per tenant. Names (anything with a space) and
        // very short terms stay on the name search only, so typing a name does not scan account names.
        string? samUrl = null;
        if (term.Length >= 4 && !term.Any(char.IsWhiteSpace))
        {
            var sam = term.Replace("'", "''", StringComparison.Ordinal);
            var samFilter = Uri.EscapeDataString(
                $"startsWith(onPremisesSamAccountName,'{sam}') and {EligibleUserFilter}");
            samUrl =
                $"https://graph.microsoft.com/v1.0/users?$filter={samFilter}&$select={UserSelect}&$count=true&$top={MaxResults}";
        }

        var tasks = new List<Task<JsonDocument?>>();
        foreach (var tenantId in tokens.TenantIds)
        {
            tasks.Add(GetDocumentAsync(tenantId, searchUrl, ct, "search"));
            if (samUrl != null)
                tasks.Add(GetDocumentAsync(tenantId, samUrl, ct, "sam-search"));
        }

        return await MergeQueries(tasks);
    }

    /// <summary>
    /// After a cancellation the other query may still be running; its document is disposed
    /// once it completes.
    /// </summary>
    internal static void DisposeWhenDone(Task<JsonDocument?> task)
    {
        if (task.IsCompleted)
        {
            if (task.IsCompletedSuccessfully)
                task.Result?.Dispose();
            return;
        }

        _ = task.ContinueWith(
            static t => t.Result?.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task<IReadOnlyList<EntraUserInfo>> MergeQueries(IReadOnlyList<Task<JsonDocument?>> tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            foreach (var task in tasks)
                DisposeWhenDone(task);
            throw;
        }

        var docs = new List<JsonDocument?>();
        try
        {
            foreach (var task in tasks)
                docs.Add(await task);
            if (docs.All(doc => doc == null))
                throw new DirectoryUnavailableException();

            return Merge(docs);
        }
        finally
        {
            foreach (var doc in docs)
                doc?.Dispose();
        }
    }

    private static EntraUserInfo[] Merge(IEnumerable<JsonDocument?> docs)
    {
        var merged = new List<EntraUserInfo>();
        var indexByUpn = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in docs)
            Add(doc);
        return merged.ToArray();

        void Add(JsonDocument? doc)
        {
            if (doc == null)
                return;

            foreach (var user in ReadUsers(doc, eligibleOnly: true))
            {
                if (!indexByUpn.TryGetValue(user.UserPrincipalName, out var index))
                {
                    indexByUpn[user.UserPrincipalName] = merged.Count;
                    merged.Add(user);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(merged[index].SamAccountName) &&
                    !string.IsNullOrWhiteSpace(user.SamAccountName))
                    merged[index] = user;
            }
        }
    }

    public async Task<EntraUserInfo?> GetUserBySamAccountNameAsync(string samAccountName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(samAccountName))
            return null;

        var sam = samAccountName.Trim().Replace("'", "''", StringComparison.Ordinal);
        var filter = Uri.EscapeDataString($"onPremisesSamAccountName eq '{sam}' and userType eq 'Member'");
        var url =
            $"https://graph.microsoft.com/v1.0/users?$filter={filter}&$select={UserSelect}&$count=true";

        var tasks = tokens.TenantIds
            .Select(tenantId => GetDocumentAsync(tenantId, url, ct, "sam-lookup"))
            .ToArray();
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            foreach (var task in tasks)
                DisposeWhenDone(task);
            throw;
        }

        var docs = tasks.Select(task => task.Result).ToArray();
        try
        {
            if (docs.All(doc => doc == null))
                throw new DirectoryUnavailableException();

            var matches = new List<EntraUserInfo>();
            var ambiguous = false;
            foreach (var doc in docs)
            {
                if (doc == null)
                    continue;

                var users = ReadUsers(doc, membersOnly: true)
                    .Where(user => user.SamAccountName != null)
                    .ToArray();
                var count = ReadCount(doc.RootElement, users.Length);
                var hasNextPage = doc.RootElement.TryGetProperty("@odata.nextLink", out _);
                if (count == 1 && users.Length == 1 && !hasNextPage)
                {
                    matches.Add(users[0]);
                    continue;
                }

                if (count > 1 || users.Length > 1 || hasNextPage)
                    ambiguous = true;
            }

            if (ambiguous || matches.Count != 1)
            {
                if (ambiguous || matches.Count > 1)
                    logger.LogWarning(
                        "Entra SAM lookup matched {Count} users; returning none",
                        Math.Max(matches.Count, ambiguous ? 2 : 0));
                return null;
            }

            return matches[0];
        }
        finally
        {
            foreach (var doc in docs)
                doc?.Dispose();
        }
    }

    public async Task<EntraLoginIdentity?> GetLoginIdentityBySamAccountNameAsync(string samAccountName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(samAccountName))
            return null;

        var sam = samAccountName.Trim().Replace("'", "''", StringComparison.Ordinal);
        var filter = Uri.EscapeDataString($"onPremisesSamAccountName eq '{sam}'");
        var url =
            $"https://graph.microsoft.com/v1.0/users?$filter={filter}&$select={UserSelect}&$count=true";

        var tasks = tokens.TenantIds
            .Select(tenantId => GetDocumentAsync(tenantId, url, ct, "login-identity"))
            .ToArray();
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            foreach (var task in tasks)
                DisposeWhenDone(task);
            throw;
        }

        var docs = tasks.Select(task => task.Result).ToArray();
        try
        {
            if (docs.All(doc => doc == null))
                return null;

            var matches = new List<EntraLoginIdentity>();
            var ambiguous = false;
            foreach (var doc in docs)
            {
                if (doc == null)
                    continue;

                var identities = ReadLoginIdentities(doc)
                    .Where(identity => identity != null)
                    .ToArray();
                var count = ReadCount(doc.RootElement, identities.Length);
                var hasNextPage = doc.RootElement.TryGetProperty("@odata.nextLink", out _);
                if (count == 1 && identities.Length == 1 && !hasNextPage)
                {
                    matches.Add(identities[0]!);
                    continue;
                }

                if (count > 1 || identities.Length > 1 || hasNextPage)
                    ambiguous = true;
            }

            if (ambiguous || matches.Count != 1)
            {
                if (ambiguous || matches.Count > 1)
                    logger.LogWarning(
                        "Entra login identity lookup matched {Count} users for SAM; treating as unknown",
                        Math.Max(matches.Count, ambiguous ? 2 : 0));
                return null;
            }

            return matches[0];
        }
        finally
        {
            foreach (var doc in docs)
                doc?.Dispose();
        }
    }

    private static EntraLoginIdentity?[] ReadLoginIdentities(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("value", out var valueElement) ||
            valueElement.ValueKind != JsonValueKind.Array)
            return [];

        return valueElement.EnumerateArray()
            .Select(TryReadLoginIdentity)
            .ToArray();
    }

    internal static EntraLoginIdentity? TryReadLoginIdentity(JsonElement user)
    {
        if (!user.TryGetProperty("onPremisesSamAccountName", out var samElement) ||
            samElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(samElement.GetString()))
            return null;

        var enabled = user.TryGetProperty("accountEnabled", out var enabledElement) &&
                      enabledElement.ValueKind == JsonValueKind.True;
        var userType = user.TryGetProperty("userType", out var userTypeElement) &&
                       userTypeElement.ValueKind == JsonValueKind.String
            ? userTypeElement.GetString()!.Trim()
            : "";

        string? accountState = null;
        string? affiliation = null;
        if (user.TryGetProperty("onPremisesExtensionAttributes", out var ext) &&
            ext.ValueKind == JsonValueKind.Object)
        {
            accountState = ReadExtensionAttribute(ext, "extensionAttribute2");
            affiliation = ReadExtensionAttribute(ext, "extensionAttribute4");
        }

        return new EntraLoginIdentity(enabled, userType, accountState, affiliation);
    }

    private static string? ReadExtensionAttribute(JsonElement ext, string name)
        => ext.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private async Task<JsonDocument?> GetDocumentAsync(string tenantId, string url, CancellationToken ct,
        string operation)
    {
        string token;
        try
        {
            token = await tokens.GetAccessTokenAsync(tenantId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Entra Graph token request failed during {Operation}", operation);
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("ConsistencyLevel", "eventual");

            var httpClient = httpClientFactory.CreateClient(ServiceCollectionExtensions.HttpClientName);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                var requestId = response.Headers.TryGetValues("request-id", out var values)
                    ? string.Join(",", values)
                    : "none";
                var status = (int)response.StatusCode;
                // 4xx other than 408/429 means Graph rejected the query or the permission, not an outage.
                var rejected = status is >= 400 and < 500 and not 408 and not 429;
                logger.Log(rejected ? LogLevel.Error : LogLevel.Warning,
                    "Entra Graph {Operation} failed with HTTP {Status}. Request id {RequestId}",
                    operation, status, requestId);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Entra Graph {Operation} failed", operation);
            return null;
        }
    }

    private static EntraUserInfo[] ReadUsers(JsonDocument doc, bool eligibleOnly = false, bool membersOnly = false)
    {
        if (!doc.RootElement.TryGetProperty("value", out var valueElement) ||
            valueElement.ValueKind != JsonValueKind.Array)
            return [];

        return valueElement.EnumerateArray()
            .Select(user => TryReadUser(user, eligibleOnly, membersOnly))
            .OfType<EntraUserInfo>()
            .ToArray();
    }

    private static int ReadCount(JsonElement root, int fallback)
    {
        if (!root.TryGetProperty("@odata.count", out var countElement))
            return fallback;
        if (countElement.TryGetInt32(out var count))
            return count;
        if (countElement.TryGetInt64(out var longCount) && longCount <= int.MaxValue)
            return (int)longCount;
        return fallback;
    }

    private static bool IsEligible(JsonElement user)
    {
        if (!user.TryGetProperty("accountEnabled", out var enabled) || enabled.ValueKind != JsonValueKind.True)
            return false;
        if (!IsMember(user))
            return false;
        if (!user.TryGetProperty("onPremisesExtensionAttributes", out var ext) ||
            ext.ValueKind != JsonValueKind.Object)
            return false;

        return AttributeEquals(ext, "extensionAttribute2", "valid")
               && AttributeEquals(ext, "extensionAttribute4", "employee");
    }

    private static bool IsMember(JsonElement user)
        => user.TryGetProperty("userType", out var userType)
           && userType.ValueKind == JsonValueKind.String
           && string.Equals(userType.GetString()?.Trim(), "Member", StringComparison.OrdinalIgnoreCase);

    private static bool AttributeEquals(JsonElement ext, string name, string expected)
        => ext.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
           && string.Equals(value.GetString()?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static EntraUserInfo? TryReadUser(JsonElement user, bool eligibleOnly = false, bool membersOnly = false)
    {
        if (eligibleOnly && !IsEligible(user))
            return null;
        if (membersOnly && !IsMember(user))
            return null;

        var upn = user.TryGetProperty("userPrincipalName", out var upnElement) ? upnElement.GetString() : null;
        var displayName = user.TryGetProperty("displayName", out var displayNameElement)
            ? displayNameElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(upn) || string.IsNullOrWhiteSpace(displayName))
            return null;

        var mail = user.TryGetProperty("mail", out var mailElement) ? mailElement.GetString() : null;
        var sam = user.TryGetProperty("onPremisesSamAccountName", out var samElement) ? samElement.GetString() : null;

        return new EntraUserInfo(
            upn,
            displayName,
            string.IsNullOrWhiteSpace(mail) ? upn : mail,
            string.IsNullOrWhiteSpace(sam) ? null : sam.Trim(),
            ReadOrganization(user));
    }

    /// <summary>
    /// Institute and department come from extensionAttribute5, 6 and 7. Values are stored
    /// wrapped in semicolons (for example <c>;FNWI;</c>), so those are removed. The result is
    /// "institute | department | sub-department", using only the attributes that have a value.
    /// </summary>
    internal static string? ReadOrganization(JsonElement user)
    {
        if (!user.TryGetProperty("onPremisesExtensionAttributes", out var ext) ||
            ext.ValueKind != JsonValueKind.Object)
            return null;

        var parts = new List<string>();
        foreach (var name in new[] { "extensionAttribute5", "extensionAttribute6", "extensionAttribute7" })
        {
            if (!ext.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
                continue;

            var cleaned = string.Join(", ", (value.GetString() ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase));
            if (cleaned.Length > 0)
                parts.Add(cleaned);
        }

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }
}