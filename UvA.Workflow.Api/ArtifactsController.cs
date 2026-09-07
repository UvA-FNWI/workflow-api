using Microsoft.AspNetCore.Authorization;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Persistence;

namespace UvA.Workflow.Api.Artifacts;

[ApiController]
[Route("[controller]")]
public class ArtifactsController(
    ArtifactTokenService artifactTokenService,
    IArtifactService artifactService) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("{artifactId}")]
    public async Task<IActionResult> Download(string artifactId, [FromQuery] string token, CancellationToken ct)
    {
        if (!await artifactTokenService.ValidateAccessToken(artifactId, token))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            return Unauthorized();
        }

        var artifact = await artifactService.GetArtifact(artifactId, ct);
        if (artifact == null) return NotFound();

        return File(artifact.Content, artifact.Info.ContentType, artifact.Info.Name);
    }
}