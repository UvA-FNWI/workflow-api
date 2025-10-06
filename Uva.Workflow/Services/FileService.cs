using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using UvA.Workflow.Api.Infrastructure.Persistence;
using Microsoft.IdentityModel.Tokens;
using Uva.Workflow.Entities.Domain;
using Uva.Workflow.Tools;
using Uva.Workflow.WorkflowInstances;

namespace Uva.Workflow.Services;

public class FileService(IConfiguration config, FileClient client)
{
    private readonly SymmetricSecurityKey _signingKey = new(Encoding.ASCII.GetBytes(config["FileKey"]!));
    private readonly string _appUrl = config["AppUrl"]!;
    
    public string GenerateUrl(StoredFile file)
        => $"{_appUrl}/WorkflowFile/Answer/{file.Id}/{file.FileName}?verifier={GenerateVerifier(file.Id.ToString())}";
    
    public string GenerateUrl(Form form, string id)
    {
        return ""; // TODO
    }
    
    public Task<string> GenerateUrl(WorkflowInstance instance)
    {
        return Task.FromResult(""); // TODO
    }
    
    private string GenerateVerifier(string id, string type = "File", int[]? allowedIds = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["id"] = id,
            ["type"] = type
        };
        if (allowedIds != null)
            claims.Add("allowedIds", allowedIds.ToSeparatedString(separator: ","));
        
        var handler = new JwtSecurityTokenHandler();
        return handler.CreateEncodedJwt(new SecurityTokenDescriptor
        {
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = "workflow",
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha512Signature),
            Claims = claims
        });
    }
    
    private Task<TokenValidationResult> GetToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        return handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            IssuerSigningKey = _signingKey,
            ValidateIssuer = true,
            ValidateAudience = false,
            ValidIssuer = "workflow"
        });
    }

    public async Task<bool> IsValid(string fileId, string token, string type)
    {
        var result = await GetToken(token); 
        return result.IsValid
               && result.Claims["id"]?.ToString() == fileId.ToString()
               && result.Claims["type"]?.ToString() == type;
    }
    
    public async Task<FileContent> GetContent(string fileId, string fileName)
    {
        var content = await client.GetFile(fileId);

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(fileName, out var type))
            type = "application/octet-stream";

        return new FileContent(content, type);
    }
}

public record FileContent(byte[] Bytes, string MediaType);