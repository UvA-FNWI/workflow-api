using System.Net;
using System.Text.Json;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Submissions.Dtos;

public record ArtifactReference(string Id, string Name, string AccessToken);

public record AnswerDto(
    string Id,
    string QuestionName,
    string FormName,
    string EntityType,
    bool IsVisible,
    BilingualString? ValidationError = null,
    JsonElement? Value = null,
    ArtifactReference[]? Files = null,
    string[]? VisibleChoices = null
);

public class AnswerDtoFactory(ArtifactTokenService artifactTokenService)
{
    public AnswerDto Create(Answer answer)
    {
        ArtifactReference[]? files = null;
        if (answer.Files != null && answer.Files.Length != 0)
        {
            files = answer.Files
                .Select(f => new ArtifactReference(f.Id.ToString(), f.Name,WebUtility.UrlEncode(artifactTokenService.CreateAccessToken(f))))
                .ToArray();
        }
        return new AnswerDto(answer.Id, answer.QuestionName, answer.FormName, answer.EntityType, answer.IsVisible, answer.ValidationError, answer.Value, files, answer.VisibleChoices);
    }
        
}