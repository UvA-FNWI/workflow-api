using UvA.Workflow.Persistence;

namespace UvA.Workflow.Api.Infrastructure;

public interface IArtifactTokenService
{
    string CreateAccessToken(ArtifactInfo artifactInfo);
    Task<bool> ValidateAccessToken(string artifactId, string token);
}