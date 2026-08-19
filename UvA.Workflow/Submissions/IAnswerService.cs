using System.Text.Json;
using Microsoft.AspNetCore.Http;
using UvA.Workflow.Persistence;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Submissions;

public interface IAnswerService
{
    Task<QuestionContext> GetQuestionContext(
        string instanceId, string submissionId, string questionName, CancellationToken ct);

    Task SavePropertyValue(
        WorkflowInstance instance,
        string[] pathParts,
        PropertyDefinition propertyDefinition,
        BsonValue newValue,
        bool shouldLog,
        CancellationToken ct);

    Task<Answer[]> SaveAnswer(QuestionContext context, JsonElement? value, CancellationToken ct);

    Task<Artifact?> GetArtifact(QuestionContext context, string artifactId, CancellationToken ct);

    Task SaveArtifact(QuestionContext context, string artifactName, Stream contents, CancellationToken ct = default);

    Task SaveArtifact(QuestionContext context, IFormFile formFile, CancellationToken ct = default);

    Task DeleteArtifact(QuestionContext context, string artifactId, CancellationToken ct);

    Task<(JsonElement? Value, UserSearchResult? CreatedUser)> ValidateAndResolveValue(
        PropertyDefinition propertyDefinition,
        JsonElement? value,
        ExternalUserInput? externalUser,
        CancellationToken ct);
}