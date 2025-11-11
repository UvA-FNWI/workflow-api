using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Submissions.Dtos;

public record SaveAnswerResponse(
    bool Success,
    Answer[] Answers,
    SubmissionDto Submission,
    string? ErrorMessage = null);

public class SaveAnswerFileRequest
{
    [FromForm] public required IFormFile File { get; set; }
}

public record SaveAnswerFileResponse(
    bool Success,
    string? ErrorMessage = null);