using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace UvA.Workflow.Api.Authentication;

public record DecodedToken(ClaimsPrincipal Principal, DateTime ExpiresAtUtc);

/// <summary>
/// HMAC signing/validation shared by the role and user impersonation tokens. They share one key and
/// issuer, so the "type" claim keeps the two kinds from being interchangeable.
/// </summary>
public class ImpersonationTokenCodec(IConfiguration config)
{
    public const string Issuer = "workflow";
    public const string TypeClaim = "type";

    private readonly SymmetricSecurityKey _signingKey = new(Encoding.ASCII.GetBytes(GetSigningKey(config)));

    private static readonly JwtSecurityTokenHandler Handler = new() { MapInboundClaims = false };

    public (string Token, DateTime ExpiresAtUtc) Encode(string type, IDictionary<string, object> claims,
        TimeSpan lifetime)
    {
        var expiresAtUtc = DateTime.UtcNow.Add(lifetime);

        var token = Handler.CreateEncodedJwt(new SecurityTokenDescriptor
        {
            Expires = expiresAtUtc,
            Issuer = Issuer,
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha512Signature),
            Claims = new Dictionary<string, object>(claims) { [TypeClaim] = type }
        });

        return (token, expiresAtUtc);
    }

    /// <summary>
    /// Validates signature, issuer, expiry and the token type, returning the decoded principal or null
    /// on any failure. The caller extracts its own remaining claims.
    /// </summary>
    public DecodedToken? Validate(string? token, string expectedType)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            var principal = Handler.ValidateToken(token, new TokenValidationParameters
            {
                IssuerSigningKey = _signingKey,
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = false,
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out var validatedToken);

            if (principal.FindFirst(TypeClaim)?.Value != expectedType)
                return null;

            return new DecodedToken(principal, validatedToken.ValidTo);
        }
        catch
        {
            return null;
        }
    }

    private static string GetSigningKey(IConfiguration config)
    {
        var key = config["ImpersonationKey"];
        if (!string.IsNullOrWhiteSpace(key))
            return key;

        throw new InvalidOperationException("Missing signing key configuration.");
    }
}