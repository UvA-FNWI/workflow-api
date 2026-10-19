using Microsoft.AspNetCore.Mvc;
using Moq;
using UvA.Workflow.Api.Artifacts;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Persistence;
using UvA.Workflow.Tests.Helpers;

namespace UvA.Workflow.Tests.Controllers;

public class ArtifactsControllerTests
{
    private readonly Mock<IArtifactService> _artifactService = new();

    private readonly ArtifactTokenService _artifactTokenService =
        new(UnitTestsHelpers.TestS3Config);

    private readonly CancellationToken _ct = new CancellationTokenSource().Token;

    [Fact]
    public async Task Download_WithValidToken_ReturnsArtifact()
    {
        var artifact = new Artifact(
            new ArtifactInfo("artifact-1", "description.docx",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            [1, 2, 3]);
        var token = _artifactTokenService.CreateAccessToken(artifact.Info);
        _artifactService.Setup(service => service.GetArtifact(artifact.Info.ArtifactId, _ct))
            .ReturnsAsync(artifact);

        var result = await Controller().Download(artifact.Info.ArtifactId, token, _ct);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(artifact.Content, file.FileContents);
        Assert.Equal(artifact.Info.ContentType, file.ContentType);
        Assert.Equal(artifact.Info.Name, file.FileDownloadName);
    }

    [Fact]
    public async Task Download_WithInvalidToken_ReturnsUnauthorized()
    {
        var result = await Controller().Download("artifact-1", "invalid-token", _ct);

        Assert.IsType<UnauthorizedResult>(result);
        _artifactService.Verify(
            service => service.GetArtifact(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Download_WithValidTokenForMissingArtifact_ReturnsNotFound()
    {
        var artifactInfo = new ArtifactInfo("missing-artifact", "missing.pdf", "application/pdf");
        var token = _artifactTokenService.CreateAccessToken(artifactInfo);
        _artifactService.Setup(service => service.GetArtifact(artifactInfo.ArtifactId, _ct))
            .ReturnsAsync((Artifact?)null);

        var result = await Controller().Download(artifactInfo.ArtifactId, token, _ct);

        Assert.IsType<NotFoundResult>(result);
    }

    private ArtifactsController Controller() => new(_artifactTokenService, _artifactService.Object);
}