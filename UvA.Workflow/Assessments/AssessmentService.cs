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
        if (assessmentConfig == null) return new AssessmentResult { PartResults = partResults };

        decimal totalPartWeight = assessmentConfig.Parts.Sum(p => p.Weight);
        var hasImportedParts = assessmentConfig.Parts.Any(part =>
            definition.AllSteps.Any(step => step.Name == part.Name && step.Import != null));
        var complete = true;

        foreach (var configuredPart in assessmentConfig.Parts)
        {
            var imported = definition.AllSteps.Any(step => step.Name == configuredPart.Name && step.Import != null);
            var owner = imported ? context.ImportedSteps.GetValueOrDefault(configuredPart.Name) : null;
            var sourcePart = owner?.Definition.AssessmentConfiguration?.Parts
                .FirstOrDefault(part => part.Name == owner.Step.Name);
            var partConfig = sourcePart == null
                ? configuredPart
                : new AssessmentPart
                {
                    Name = configuredPart.Name,
                    Title = configuredPart.Title ?? sourcePart.Title,
                    Weight = configuredPart.Weight,
                    MaximumDiscrepancy = sourcePart.MaximumDiscrepancy,
                    Sources = sourcePart.Sources
                };
            var partContext = owner?.Context ?? context;
            var partDefinition = owner?.Definition ?? definition;
            var forms = partConfig.Sources
                .Where(source => imported ? owner != null : formNames == null || formNames.Contains(source.Name))
                .Select(source => partDefinition.Forms.FirstOrDefault(c => c.Name == source.Name))
                .Where(f => f != null)
                .Cast<Form>()
                .Where(form => !hasImportedParts || FormSubmissionState.GetSubmissionEventIds(form)
                    .Any(id => partContext.Get(id + "Event") is DateTime))
                .ToList();
            var sourceResults = forms
                .Select(c => AssessmentHelpers.CalculateSourceResult(c, partContext, pageName))
                .ToList();
            // The grade helpers treat zero results as missing when averaging.
            complete &= partConfig.Sources.Count > 0 && sourceResults.Count == partConfig.Sources.Count &&
                        sourceResults.All(source => source.WeightedAverage != 0);

            var result = new AssessmentPartResult
            {
                Name = partConfig.Name,
                Combined = AssessmentHelpers.CalculateCombined(partConfig, sourceResults),
                SourceResults = sourceResults,
                PartPercentage = totalPartWeight > 0
                    ? partConfig.Weight / totalPartWeight * 100
                    : 0,
                PartConfig = partConfig,
                Owner = owner
            };
            partResults.Add(result);
        }

        var (calculatedFinalGradeUnrounded, calculatedFinalGradeRounded) =
            AssessmentHelpers.CalculateFinalGrade(assessmentConfig, partResults);

        return new AssessmentResult
        {
            PartResults = partResults,
            FinalGradeRounded = !hasImportedParts || complete ? calculatedFinalGradeRounded : null,
            FinalGradeUnrounded = !hasImportedParts || complete ? calculatedFinalGradeUnrounded : null,
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