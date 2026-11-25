using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;

namespace UvA.Workflow.Api.WorkflowAuthoring;

public sealed class GitHubAppToken(
    IHttpClientFactory clients,
    IOptions<WorkflowAuthoringOptions> options)
{
    private readonly SemaphoreSlim _lock = new(1);
    private InstallationToken? _token;

    internal const string ClientName = "WorkflowAuthoringGitHub";

    public async Task<string> Get(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_token is { } cached && cached.ExpiresAt > now.AddMinutes(2))
                return cached.Token;

            var settings = options.Value;
            using var rsa = RSA.Create();
            try
            {
                rsa.ImportFromPem(settings.GitHubPrivateKey);
            }
            catch (ArgumentException)
            {
                throw new WorkflowAuthoringException(503, "AuthoringUnavailable", "The GitHub App key is invalid.");
            }

            var jwt = new JwtSecurityToken(new JwtHeader(new SigningCredentials(new RsaSecurityKey(rsa),
                SecurityAlgorithms.RsaSha256)), new JwtPayload
            {
                [JwtRegisteredClaimNames.Iss] = settings.GitHubAppId,
                [JwtRegisteredClaimNames.Iat] = now.AddSeconds(-60).ToUnixTimeSeconds(),
                [JwtRegisteredClaimNames.Exp] = now.AddMinutes(8).ToUnixTimeSeconds()
            });

            var repo = WorkflowAuthoringOptions.RepositoryName(settings.RepoUrl)!.Split('/')[1];
            using var client = clients.CreateClient(ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"app/installations/{settings.GitHubInstallationId}/access_tokens")
            {
                Content = JsonContent.Create(new
                {
                    repositories = new[] { repo },
                    permissions = new { contents = "write", pull_requests = "write" }
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                new JwtSecurityTokenHandler().WriteToken(jwt));
            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            _token = await response.Content.ReadFromJsonAsync<InstallationToken>(ct)
                     ?? throw new HttpRequestException("GitHub returned no installation token.");
            return _token.Token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed record InstallationToken(
        string Token,
        [property: JsonPropertyName("expires_at")]
        DateTimeOffset ExpiresAt);
}