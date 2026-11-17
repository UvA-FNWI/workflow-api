using UvA.Workflow.Assessments;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.WorkflowInstances;

/// <summary>
/// A group of workflow instances that need extra values before their text and conditions can be evaluated.
/// Groups can be handled together so shared values only have to be loaded once.
/// </summary>
/// <param name="WorkflowDefinition">Describes the properties available on these instances.</param>
/// <param name="Contexts">Contains the instance values and receives the loaded values.</param>
/// <param name="Lookups">The values used by text, conditions, or the response.</param>
public sealed record EnrichmentBatch(
    WorkflowDefinition WorkflowDefinition,
    ICollection<ObjectContext> Contexts,
    IEnumerable<Lookup> Lookups);

public class WorkflowContextEnricher(
    IWorkflowInstanceRepository workflowInstanceRepository,
    ModelService modelService,
    IAssessmentService assessmentService)
{
    public async Task Enrich(
        IEnumerable<EnrichmentBatch> batches,
        CancellationToken ct,
        bool replaceStep = true)
    {
        // Assessments need some course settings in addition to the values requested by the caller.
        var states = batches.Select(batch =>
        {
            var lookups = batch.WorkflowDefinition.GetSourceLookups(batch.Lookups).ToList();
            var hasAssessment = lookups.Any(lookup =>
                                    lookup is PropertyLookup property && property.Parts[0] == "Assessment")
                                && batch.WorkflowDefinition.AssessmentConfiguration != null;
            if (hasAssessment)
                lookups.AddRange(
                    new PropertyLookup("Course.GradingBasis"),
                    new PropertyLookup("Course.GradeGap"));
            return new EnrichmentState(batch, lookups, hasAssessment);
        }).ToArray();

        await ContextLoader.LoadReferences(modelService, workflowInstanceRepository,
            states.Select(state => state.Batch with { Lookups = state.Lookups }), ct);

        // Calculate assessment values using the settings of the original workflow instances.
        foreach (var state in states.Where(state => state.HasAssessment))
        {
            foreach (var context in state.Batch.Contexts)
            {
                var config = state.Batch.WorkflowDefinition.AssessmentConfiguration!
                    .Enrich(context.Get("Course.GradingBasis") as string, context.Get("Course.GradeGap") as bool?);
                var result = assessmentService.GetAssessmentResult(
                    state.Batch.WorkflowDefinition,
                    context,
                    config);
                var dict = result.PartResults.ToDictionary(r => r.Name,
                    object? (r) => r.AllResults.ToDictionary(s => s.Name,
                        object (s) =>
                        {
                            var questionResults = s.PageResults
                                .SelectMany(p => p.QuestionResults)
                                .ToDictionary(q => q.Name, object (q) => q.Answer);
                            questionResults.Add("WeightedAverage", s.WeightedAverage);
                            return questionResults;
                        }));
                dict["FinalGrade"] = result.FinalGradeRounded;
                context.Values["Assessment"] = dict;
            }
        }

        foreach (var state in states)
        foreach (var context in state.Batch.Contexts)
            context.ComputeProperties(state.Batch.WorkflowDefinition);

        // Replace internal step names using the titles from the original workflow definition.
        if (replaceStep)
        {
            foreach (var state in states)
            {
                foreach (var context in state.Batch.Contexts)
                {
                    if (context.Values.TryGetValue("CurrentStep", out var i) && i is string stepName)
                        context.Values["CurrentStep"] =
                            context.ImportedSteps.GetValueOrDefault(stepName)?.Step.DisplayTitle ??
                            state.Batch.WorkflowDefinition.AllSteps.GetOrDefault(stepName)?.DisplayTitle ?? stepName;
                }
            }
        }
    }

    private sealed record EnrichmentState(
        EnrichmentBatch Batch,
        List<Lookup> Lookups,
        bool HasAssessment);
}