using UvA.Workflow.Api.Authentication;

namespace UvA.Workflow.Api.WorkflowInstances;

public static class RoleImpersonationConstants
{
    public const string HeaderName = "X-Workflow-Impersonation";
    public const string TokenType = "workflow-impersonation";
    public const string UserClaim = "user";
    public const string InstanceClaim = "instance";
    public const string RoleClaim = "role";
}

public record RoleImpersonationToken(string Value, DateTime ExpiresAtUtc);

public record RoleImpersonationTokenClaims(
    string UserName,
    string InstanceId,
    string RoleName,
    DateTime ExpiresAtUtc);

public class RoleImpersonationService(
    IConfiguration config,
    IHttpContextAccessor httpContextAccessor,
    IUserService userService)
    : IImpersonationContextService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(8);

    private readonly ImpersonationTokenCodec _codec = new(config);

    public RoleImpersonationToken CreateToken(string userName, string instanceId, string roleName)
    {
        var claims = new Dictionary<string, object>
        {
            [RoleImpersonationConstants.UserClaim] = userName,
            [RoleImpersonationConstants.InstanceClaim] = instanceId,
            [RoleImpersonationConstants.RoleClaim] = roleName
        };

        var (token, expiresAtUtc) = _codec.Encode(RoleImpersonationConstants.TokenType, claims, TokenLifetime);
        return new RoleImpersonationToken(token, expiresAtUtc);
    }

    public Task<RoleImpersonationTokenClaims?> ValidateToken(string token) => Task.FromResult(Decode(token));

    private RoleImpersonationTokenClaims? Decode(string token)
    {
        var decoded = _codec.Validate(token, RoleImpersonationConstants.TokenType);
        if (decoded is null)
            return null;

        var principal = decoded.Principal;
        var userName = principal.FindFirst(RoleImpersonationConstants.UserClaim)?.Value;
        var instanceId = principal.FindFirst(RoleImpersonationConstants.InstanceClaim)?.Value;
        var roleName = principal.FindFirst(RoleImpersonationConstants.RoleClaim)?.Value;
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(instanceId) ||
            string.IsNullOrWhiteSpace(roleName))
            return null;

        return new RoleImpersonationTokenClaims(userName, instanceId, roleName, decoded.ExpiresAtUtc);
    }

    public async Task<string?> GetImpersonatedRole(WorkflowInstance instance, CancellationToken ct = default)
    {
        var token = httpContextAccessor.HttpContext?.Request.Headers[RoleImpersonationConstants.HeaderName]
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var impersonationClaims = await ValidateToken(token);
        if (impersonationClaims == null)
            return null;

        var user = await userService.GetCurrentUser(ct);
        if (user == null)
            return null;

        if (!string.Equals(user.UserName, impersonationClaims.UserName, StringComparison.Ordinal))
            return null;

        if (!string.Equals(instance.Id, impersonationClaims.InstanceId, StringComparison.Ordinal))
            return null;

        return impersonationClaims.RoleName;
    }
}