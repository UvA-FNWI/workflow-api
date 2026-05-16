using System.Text.Json;
using System.Text.Json.Serialization;

namespace UvA.Workflow.Users.EduId;

public interface IEduIdInvitationClient
{
    Task<EduIdInvitationResponse> CreateInvitationAsync(EduIdInvitationRequest request, CancellationToken ct = default);
}

public class EduIdInvitationClient(IHttpClientFactory httpClientFactory, ILogger<EduIdInvitationClient> logger)
    : IEduIdInvitationClient
{
    public const string HttpClientName = "EduIdInvitationClient";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<EduIdInvitationResponse> CreateInvitationAsync(EduIdInvitationRequest request,
        CancellationToken ct = default)
    {
        using var response = await httpClientFactory.CreateClient(HttpClientName)
            .PostAsJsonAsync("/api/external/v1/invitations", request, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"EduID invitation failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}",
                null,
                response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<EduIdInvitationResponse>(JsonOptions, ct);
        if (payload is null)
            throw new InvalidOperationException("Failed to deserialize the EduID invitation response.");

        logger.LogInformation("Created EduID invitation for {Count} recipients", request.Invites.Count);
        return payload;
    }
}

public sealed class EduIdInvitationRequest
{
    [JsonPropertyName("intendedAuthority")]
    public string IntendedAuthority { get; init; } = null!;

    [JsonPropertyName("message")] public string Message { get; init; } = null!;

    [JsonPropertyName("language")] public string Language { get; init; } = null!;

    [JsonPropertyName("enforceEmailEquality")]
    public bool? EnforceEmailEquality { get; init; }

    [JsonPropertyName("eduIDOnly")] public bool? EduIdOnly { get; init; }

    [JsonPropertyName("guestRoleIncluded")]
    public bool? GuestRoleIncluded { get; init; }

    [JsonPropertyName("suppressSendingEmails")]
    public bool? SuppressSendingEmails { get; init; }

    [JsonPropertyName("invites")] public List<string> Invites { get; init; } = [];

    [JsonPropertyName("roleIdentifiers")] public List<long> RoleIdentifiers { get; init; } = [];

    [JsonPropertyName("organizationGUID")] public string OrganizationGuid { get; init; } = string.Empty;

    [JsonPropertyName("roleExpiryDate")] public DateTime? RoleExpiryDate { get; init; }

    [JsonPropertyName("expiryDate")] public DateTime ExpiryDate { get; init; }
}

public sealed record EduIdInvitationResponse(
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("recipientInvitationURLs")]
    List<EduIdRecipientInvitationUrl>? RecipientInvitationUrls
);

public sealed record EduIdRecipientInvitationUrl(
    [property: JsonPropertyName("recipient")]
    string Recipient,
    [property: JsonPropertyName("invitationURL")]
    string InvitationUrl
);