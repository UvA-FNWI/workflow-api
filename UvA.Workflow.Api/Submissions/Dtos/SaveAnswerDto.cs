using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Submissions.Dtos;

public record FileUpload(
    string FileName,
    string ContentBase64);

public record SaveAnswerRequest(
    string InstanceId,
    string SubmissionId,
    AnswerInput Answer);

public record SaveAnswerResponse(
    bool Success,
    Answer[] Answers,
    SubmissionDto Submission,
    string? ErrorMessage = null);
    
public record SaveAnswerFileResponse(
    bool Success,
    string? ErrorMessage = null);    