using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using UvA.Workflow.Files.S3;
using UvA.Workflow.Infrastructure;

namespace UvA.Workflow.Api.Infrastructure;

public class S3TokenService(
    IOptionsMonitor<S3Config> s3ConfigOptions,
    ILogger<S3TokenService> logger)
{
    private readonly S3Config _s3ConfigOptions = s3ConfigOptions.CurrentValue;

    public string CreateS3AccessToken(string bucket, string key, int expiresInMinutes = 60)
    {
        var claims = new List<Claim>
        {
            new("bucket", bucket),
            new("key", key),
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiresInMinutes),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_s3ConfigOptions.SigningKey)),
                SecurityAlgorithms.HmacSha256)
        );

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(token);
    }

    public ClaimsPrincipal? ValidateS3AccessToken(string token)
    {
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_s3ConfigOptions.SigningKey));
        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
        };

        try
        {
            var principal = tokenHandler.ValidateToken(token, validationParameters, out var _);
            logger.LogDebug("S3 access token validated successfully");
            return principal;
        }
        catch (SecurityTokenException ex)
        {
            logger.LogWarning(ex, "S3 access token validation failed: SecurityTokenException");
            return null;
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "S3 access token validation failed: ArgumentException");
            return null;
        }
    }

    public bool ValidateS3Access(ClaimsPrincipal principal, string requestedBucket, string requestedKey)
    {
        var tokenBucket = principal.FindFirst("bucket")?.Value;
        var tokenKey = principal.FindFirst("key")?.Value;

        var hasAccess = requestedBucket == tokenBucket && requestedKey == tokenKey;

        if (!hasAccess)
        {
            logger.LogWarning("S3 access validation failed. Token bucket: {TokenBucket}, Token key: {TokenKey}, " +
                              "Requested bucket: {RequestedBucket}, Requested key: {RequestedKey}",
                tokenBucket,
                tokenKey,
                requestedBucket,
                requestedKey);
        }
        else
        {
            logger.LogDebug("S3 access validation successful for bucket: {Bucket}, key: {Key}",
                requestedBucket,
                requestedKey);
        }

        return hasAccess;
    }

    public string CreateS3AccessLink(string bucket, string key, int expiresInMinutes = 60)
    {
        var token = CreateS3AccessToken(bucket, key, expiresInMinutes);
        var baseUrl = ""; //_dataNoseSettings.ApplicationUrl.TrimEnd('/');
        return $"/api/files/getfile?bucket={bucket}&key={key}&token={token}";
    }
}