using System.Text.Json;
using UvA.Workflow.Submissions;
using UvA.Workflow.Api.Users.Dtos;

namespace UvA.Workflow.Api.Submissions.Dtos;

public record SaveAnswerRequest(
    JsonElement? Value,
    int? DeleteFileId = null,
    CreateExternalUserDto? ExternalUser = null);

public record SaveAnswerResponse(
    bool Success,
    Answer[] Answers,
    SubmissionDto Submission,
    string? ErrorMessage = null,
    UserSearchResultDto? User = null);

public class SaveAnswerFileRequest
{
    [FromForm] public required IFormFile File { get; set; }
}

public record SaveAnswerFileResponse(
    bool Success,
    string? ErrorMessage = null);