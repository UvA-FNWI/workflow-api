namespace UvA.Workflow.Api.Authentication;

public static class UserImpersonationConstants
{
    public const string HeaderName = "X-User-Impersonation";
    public const string TokenType = "user-impersonation";
    public const string RealUserClaim = "realUser";
    public const string TargetUserClaim = "targetUser";
}

public record UserImpersonationToken(string Value, DateTime ExpiresAtUtc);

/// <summary>
/// Mints and validates the token that lets a super admin act as another user. The token names the
/// real admin so it only works in that admin's own session.
/// </summary>
public class UserImpersonationTokenService(IConfiguration config, IHttpContextAccessor httpContextAccessor)
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(8);

    private readonly ImpersonationTokenCodec _codec = new(config);

    public UserImpersonationToken CreateToken(string realUserName, string targetUserName)
    {
        var claims = new Dictionary<string, object>
        {
            [UserImpersonationConstants.RealUserClaim] = realUserName,
            [UserImpersonationConstants.TargetUserClaim] = targetUserName
        };

        var (token, expiresAtUtc) = _codec.Encode(UserImpersonationConstants.TokenType, claims, TokenLifetime);
        return new UserImpersonationToken(token, expiresAtUtc);
    }

    /// <summary>
    /// Returns the user being impersonated per the request header, or null when no valid impersonation
    /// is active for <paramref name="realUserName"/>.
    /// </summary>
    public string? TryResolveTargetUser(string realUserName)
    {
        var token = httpContextAccessor.HttpContext?.Request.Headers[UserImpersonationConstants.HeaderName]
            .FirstOrDefault();

        var principal = _codec.Validate(token, UserImpersonationConstants.TokenType)?.Principal;
        if (principal is null)
            return null;

        // Only honoured for the admin it was issued to.
        var realUser = principal.FindFirst(UserImpersonationConstants.RealUserClaim)?.Value;
        if (!string.Equals(realUser, realUserName, StringComparison.Ordinal))
            return null;

        var targetUser = principal.FindFirst(UserImpersonationConstants.TargetUserClaim)?.Value;
        return string.IsNullOrWhiteSpace(targetUser) ? null : targetUser;
    }
}

/// <summary>
/// Resolves the current user to the impersonated target when a valid token is present, otherwise to
/// the real user. The authenticated principal itself is left untouched.
/// </summary>
public class ImpersonatingCurrentUserAccessor(
    HttpContextCurrentUserAccessor inner,
    UserImpersonationTokenService tokenService) : ICurrentUserAccessor
{
    private bool _resolved;
    private string? _userName;

    public string? GetCurrentUserName()
    {
        // Resolve once per request (scoped service, the header doesn't change).
        if (_resolved)
            return _userName;

        var realName = inner.GetCurrentUserName();
        _userName = string.IsNullOrWhiteSpace(realName)
            ? realName
            : tokenService.TryResolveTargetUser(realName) ?? realName;
        _resolved = true;
        return _userName;
    }
}