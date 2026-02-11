using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace UvA.Workflow.Api.WorkflowInstances;

public static class ImpersonationConstants
{
    public const string HeaderName = "X-Workflow-Impersonation";
    public const string TokenType = "workflow-impersonation";
    public const string TokenIssuer = "workflow";
    public const string TypeClaim = "type";
    public const string UserClaim = "user";
    public const string InstanceClaim = "instance";
    public const string RoleClaim = "role";
}

public record ImpersonationToken(string Value, DateTime ExpiresAtUtc);

public record ImpersonationTokenClaims(
    string UserName,
    string InstanceId,
    string RoleName,
    DateTime ExpiresAtUtc);

public class ImpersonationService(
    IConfiguration config,
    IHttpContextAccessor httpContextAccessor,
    IUserService userService,
    ModelService modelService)
    : IImpersonationContextService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(8);

    private readonly SymmetricSecurityKey signingKey = new(Encoding.ASCII.GetBytes(GetSigningKey(config)));

    public ImpersonationToken CreateToken(string userName, string instanceId, string roleName)
    {
        var expiresAtUtc = DateTime.UtcNow.Add(TokenLifetime);

        var claims = new Dictionary<string, object>
        {
            [ImpersonationConstants.TypeClaim] = ImpersonationConstants.TokenType,
            [ImpersonationConstants.UserClaim] = userName,
            [ImpersonationConstants.InstanceClaim] = instanceId,
            [ImpersonationConstants.RoleClaim] = roleName
        };

        var token = new JwtSecurityTokenHandler().CreateEncodedJwt(new SecurityTokenDescriptor
        {
            Expires = expiresAtUtc,
            Issuer = ImpersonationConstants.TokenIssuer,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha512Signature),
            Claims = claims
        });

        return new ImpersonationToken(token, expiresAtUtc);
    }

    public async Task<ImpersonationTokenClaims?> ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        TokenValidationResult result;
        try
        {
            var handler = new JwtSecurityTokenHandler
            {
                MapInboundClaims = false
            };
            result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
            {
                IssuerSigningKey = signingKey,
                ValidateIssuer = true,
                ValidIssuer = ImpersonationConstants.TokenIssuer,
                ValidateAudience = false,
                ClockSkew = TimeSpan.FromMinutes(1)
            });
        }
        catch
        {
            return null;
        }

        if (!result.IsValid)
            return null;

        if (!TryGetClaim(result.Claims, ImpersonationConstants.TypeClaim, out var type) ||
            !string.Equals(type, ImpersonationConstants.TokenType, StringComparison.Ordinal))
            return null;

        if (!TryGetClaim(result.Claims, ImpersonationConstants.UserClaim, out var userName) ||
            !TryGetClaim(result.Claims, ImpersonationConstants.InstanceClaim, out var instanceId) ||
            !TryGetClaim(result.Claims, ImpersonationConstants.RoleClaim, out var roleName))
            return null;

        var expiresAtUtc = result.SecurityToken?.ValidTo ?? DateTime.MinValue;
        return new ImpersonationTokenClaims(userName, instanceId, roleName, expiresAtUtc);
    }

    public async Task<string?> GetImpersonatedRole(WorkflowInstance instance, CancellationToken ct = default)
    {
        var token = httpContextAccessor.HttpContext?.Request.Headers[ImpersonationConstants.HeaderName]
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

        return NormalizeWorkflowRelevantRole(instance.WorkflowDefinition, impersonationClaims.RoleName)?.Name;
    }

    private static bool TryGetClaim(IDictionary<string, object> claims, string claimName, out string value)
    {
        value = "";
        if (!claims.TryGetValue(claimName, out var rawValue))
            return false;

        value = rawValue.ToString() ?? "";
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string GetSigningKey(IConfiguration config)
    {
        var key = config["ImpersonationKey"];
        if (!string.IsNullOrWhiteSpace(key))
            return key;

        throw new InvalidOperationException("Missing signing key configuration.");
    }

    private WorkflowImpersonationRole? NormalizeWorkflowRelevantRole(string workflowDefinition, string roleName)
    {
        if (!modelService.WorkflowDefinitions.TryGetValue(workflowDefinition, out var definition))
            return null;

        var actionRoles = modelService.Roles.Values
            .Where(r => r.Actions.Any(a => a.WorkflowDefinition == null || a.WorkflowDefinition == workflowDefinition))
            .Select(r => r.Name);

        var definitionRoles = definition.Properties
            .Where(p => p.DataType == DataType.User)
            .Select(p => p.Name)
            .Concat(definition.Properties.SelectMany(p => p.InheritedRoles));

        return actionRoles
            .Concat(definitionRoles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(r => modelService.Roles.GetValueOrDefault(r))
            .Where(r => r != null)
            .Select(r => new WorkflowImpersonationRole(r!.Name, r.DisplayTitle))
            .FirstOrDefault(r => string.Equals(r.Name, roleName, StringComparison.OrdinalIgnoreCase));
    }
}