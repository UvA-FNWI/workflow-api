using System.Text.Json.Serialization;

namespace UvA.Workflow.Api.Authentication;

public sealed record IntrospectionResponse(
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("acr")] string? Acr,
    [property: JsonPropertyName("authenticating_authority")]
    string? AuthenticatingAuthority,
    [property: JsonPropertyName("client_id")]
    string? ClientId,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("email_verified")]
    bool? EmailVerified,
    [property: JsonPropertyName("exp")] long? Exp,
    [property: JsonPropertyName("iss")] string? Iss,
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("sub")] string? Sub,
    [property: JsonPropertyName("token_type")]
    string? TokenType,
    [property: JsonPropertyName("uids")] string[]? Uids,
    [property: JsonPropertyName("updated_at")]
    long? UpdatedAt,
    [property: JsonPropertyName("name")] string FullName
);