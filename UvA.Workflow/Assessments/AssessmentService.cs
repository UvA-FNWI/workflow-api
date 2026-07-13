using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Assessments;

public class AssessmentService(
    ModelService modelService,
    WorkflowInstanceService workflowInstanceService,
    IWorkflowInstanceRepository instanceRepository
) : IAssessmentService
{
    public AssessmentResult GetAssessmentResult(
        WorkflowDefinition definition,
        ObjectContext context,
        AssessmentConfiguration? assessmentConfig,
        List<string>? formNames = null,
        string? pageName = null
    )
    {
        var partResults = new List<AssessmentPartResult>();
        decimal totalPartWeight = assessmentConfig?.Parts.Sum(p => p.Weight) ?? 0;

        foreach (var partConfig in assessmentConfig?.Parts ?? [])
        {
            var forms = partConfig.Sources
                .Where(source => formNames == null || formNames.Contains(source.Name))
                .Select(source => definition.Forms.FirstOrDefault(c => c.Name == source.Name))
                .Where(f => f != null)
                .Cast<Form>()
                .ToList();

            var sourceResults = forms
                .Select(c => AssessmentHelpers.CalculateSourceResult(c, context, pageName))
                .ToList();

            var result = new AssessmentPartResult
            {
                Name = partConfig.Name,
                Combined = AssessmentHelpers.CalculateCombined(partConfig, sourceResults),
                SourceResults = sourceResults,
                PartPercentage = totalPartWeight > 0
                    ? partConfig.Weight / totalPartWeight * 100
                    : 0,
                PartConfig = partConfig
            };
            partResults.Add(result);
        }

        if (assessmentConfig == null) return new AssessmentResult { PartResults = partResults };

        var (calculatedFinalGradeUnrounded, calculatedFinalGradeRounded) =
            AssessmentHelpers.CalculateFinalGrade(assessmentConfig, partResults);

        return new AssessmentResult
        {
            PartResults = partResults,
            FinalGradeRounded = calculatedFinalGradeRounded,
            FinalGradeUnrounded = calculatedFinalGradeUnrounded,
            AssessmentConfiguration = assessmentConfig
        };
    }

    public async Task<AssessmentConfiguration?> GetEnrichedAssessmentConfig(WorkflowInstance instance,
        CancellationToken ct)
    {
        var workflowDefinition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var assessmentConfig = workflowDefinition.AssessmentConfiguration;
        if (assessmentConfig == null) return null;

        var course = instance.GetProperty("Course");
        if (course?.IsString != true) return assessmentConfig;
        var courseInstance = await instanceRepository.GetById(course.AsString, ct);
        if (courseInstance == null) return assessmentConfig;

        var gradingBasis = courseInstance.GetProperty("GradingBasis");
        var gradeGap = courseInstance.GetProperty("GradeGap");

        return assessmentConfig.Enrich(
            gradingBasis?.IsString == true ? gradingBasis.AsString : null,
            gradeGap?.IsBoolean == true ? gradeGap.AsBoolean : null
        );
    }

    public async Task<SubmissionContext[]> ResolveAssessmentContexts(
        WorkflowInstance instance,
        string requestedId,
        CancellationToken ct)
    {
        // If the form with the name exists, return the single form
        if (modelService.TryGetForm(instance, requestedId) != null)
        {
            var context = await workflowInstanceService.GetSubmissionContext(instance.Id, requestedId, null, ct);
            return [context];
        }

        // Otherwise treat requestedId as base form and resolve child forms
        var childForms = modelService.GetDerivedForms(instance, requestedId).ToArray();
        if (childForms.Length == 0)
            throw new ArgumentException($"Form with {requestedId} not found");

        var contexts = await Task.WhenAll(
            childForms.Select(f => workflowInstanceService.GetSubmissionContext(instance.Id, f.Name, null, ct))
        );

        return contexts;
    }
}