using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Assessments;

public interface IAssessmentService
{
    AssessmentResult GetAssessmentResult(
        WorkflowDefinition definition,
        ObjectContext context,
        AssessmentConfiguration? assessmentConfig,
        List<string>? formNames = null,
        string? pageName = null
    );

    Task<AssessmentConfiguration?> GetEnrichedAssessmentConfig(WorkflowInstance instance,
        CancellationToken ct);

    Task<SubmissionContext[]> ResolveAssessmentContexts(
        WorkflowInstance instance,
        string requestedId,
        CancellationToken ct);
}