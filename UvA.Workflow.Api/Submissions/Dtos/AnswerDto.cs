using System.Net;
using System.Text.Json;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Submissions.Dtos;

public record ArtifactReference(string Id, string Name, string AccessToken);

public record AnswerChangeDto(JsonElement? Value, DateTime ChangedAt, string? ChangedBy);

public record AnswerChangeGroupDto(int VersionNumber, AnswerChangeDto[] Changes);

public record AnswerDto(
    string Id,
    string QuestionName,
    string FormName,
    string WorkflowDefinition,
    bool IsVisible,
    BilingualString? ValidationError = null,
    JsonElement? Value = null,
    ArtifactReference[]? Files = null,
    string[]? VisibleChoices = null,
    AnswerChangeGroupDto[]? Changes = null
);

public class AnswerDtoFactory(ArtifactTokenService artifactTokenService)
{
    public AnswerDto Create(Answer answer, AnswerChangeGroupDto[]? changes = null)
    {
        ArtifactReference[]? files = null;
        if (answer.Files != null && answer.Files.Length != 0)
        {
            var validFiles = answer.Files
                .Where(f => !string.IsNullOrWhiteSpace(f.ArtifactId))
                .ToArray();
            files = validFiles
                .Select(f => new ArtifactReference(f.ArtifactId, f.Name,
                    WebUtility.UrlEncode(artifactTokenService.CreateAccessToken(f))))
                .ToArray();
        }

        return new AnswerDto(answer.Id, answer.QuestionName, answer.FormName, answer.WorkflowDefinition,
            answer.IsVisible,
            answer.ValidationError, answer.Value, files, answer.VisibleChoices, changes);
    }
}