using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using UvA.Workflow.Infrastructure.S3;
using UvA.Workflow.Persistence;

namespace UvA.Workflow.Api.Infrastructure;

public class S3ArtifactTokenService(IOptionsMonitor<S3Config> s3ConfigOptions)
    : IArtifactTokenService
{
    private const string TokenIssuer = "workflow";
    private const string ResourceType = "artefact";

    private readonly S3Config _s3ConfigOptions = s3ConfigOptions.CurrentValue;

    public string CreateAccessToken(ArtifactInfo artifactInfo)
    {
        var claims = new List<Claim>
        {
            new("type", ResourceType),
            new("bucket", Buckets.Resumes),
            new("key", artifactInfo.Id.ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: TokenIssuer,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(60),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_s3ConfigOptions.SigningKey)),
                SecurityAlgorithms.HmacSha256)
        );

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(token);
    }

    public async Task<bool> ValidateAccessToken(string artifactId, string token)
    {
        if (string.IsNullOrWhiteSpace(token) ||
            string.IsNullOrWhiteSpace(artifactId))
            return false;
        var handler = new JwtSecurityTokenHandler();
        var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_s3ConfigOptions.SigningKey)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
        });
        return result.IsValid
               && result.Claims["key"]?.ToString() == artifactId
               && result.Claims["type"]?.ToString() == ResourceType;
    }
}