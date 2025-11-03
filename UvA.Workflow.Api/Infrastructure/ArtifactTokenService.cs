using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using UvA.Workflow.Persistence;

namespace UvA.Workflow.Api.Infrastructure;

public class ArtifactTokenService(IConfiguration config)
{
    private const string ResourceType = "artefact";
    private const string TokenIssuer = "workflow";
    private readonly SymmetricSecurityKey signingKey = new(Encoding.ASCII.GetBytes(config["FileKey"]!));

    public string CreateAccessToken(ArtifactInfo artifactInfo)
    {
        var claims = new Dictionary<string, object>
        {
            ["id"] = artifactInfo.Id.ToString(),
            ["type"] = ResourceType
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.CreateEncodedJwt(new SecurityTokenDescriptor
        {
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = TokenIssuer,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha512Signature),
            Claims = claims
        });
    }

    public async Task<bool> ValidateAccessToken(string artifactId, string token)
    {
        if (string.IsNullOrWhiteSpace(token) ||
            string.IsNullOrWhiteSpace(artifactId))
            return false;
        var handler = new JwtSecurityTokenHandler();
        var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            IssuerSigningKey = signingKey,
            ValidateIssuer = true,
            ValidateAudience = false,
            ValidIssuer = TokenIssuer
        });
        return result.IsValid
               && result.Claims["id"]?.ToString() == artifactId
               && result.Claims["type"]?.ToString() == ResourceType;
    }
}