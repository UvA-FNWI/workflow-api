using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Assessments;

public interface IAssessmentService
{
    AssessmentResult GetAssessmentResult(
        ObjectContext context,
        ICollection<SubmissionContext> contextList,
        AssessmentConfiguration? assessmentConfig,
        string? pageName
    );

    AssessmentResult GetAssessmentResult(
        WorkflowDefinition definition,
        ObjectContext context,
        AssessmentConfiguration? assessmentConfig,
        string? pageName
    );

    Task<AssessmentConfiguration?> GetEnrichedAssessmentConfig(WorkflowInstance instance,
        CancellationToken ct);

    Task<SubmissionContext[]> ResolveAssessmentContexts(
        WorkflowInstance instance,
        string requestedId,
        CancellationToken ct);
}