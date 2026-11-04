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
            var lookups = batch.Lookups.ToList();
            var hasAssessment = lookups.Any(lookup =>
                                    lookup is PropertyLookup property && property.Parts[0] == "Assessment")
                                && batch.WorkflowDefinition.AssessmentConfiguration != null;
            if (hasAssessment)
                lookups.AddRange(
                    new PropertyLookup("Course.GradingBasis"),
                    new PropertyLookup("Course.GradeGap"));
            return new EnrichmentState(batch, lookups, hasAssessment);
        }).ToArray();

        var referenceEnrichments = states
            .SelectMany(GetReferenceEnrichments)
            .ToArray();

        // Work out which referenced instances and values are needed. All workflow types are stored together,
        // so they can be loaded in one database call even when the source instances have different types.
        var targetGroups = referenceEnrichments
            .GroupBy(enrichment => enrichment.TargetDefinition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var referenceIds = targetGroups
            .SelectMany(targetGroup => targetGroup
                .SelectMany(enrichment => enrichment.Contexts.SelectMany(context =>
                    GetReferenceIds(context.Get(enrichment.ReferenceProperty)))))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (referenceIds.Length > 0)
        {
            var projectedProperties = targetGroups
                .SelectMany(targetGroup => targetGroup.SelectMany(enrichment => enrichment.ProjectedProperties))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var results = await workflowInstanceRepository.GetAllById(
                referenceIds,
                projectedProperties.ToDictionary(property => property, property => $"$Properties.{property}"),
                ct);
            var resultsById = results.ToDictionary(result => result["_id"].ToString()!);

            // Interpret each result using its own workflow definition, then add it to the instances that use it.
            foreach (var targetGroup in targetGroups)
            {
                var targetDefinition = targetGroup.First().TargetDefinition;
                var targetIds = targetGroup
                    .SelectMany(enrichment => enrichment.Contexts.SelectMany(context =>
                        GetReferenceIds(context.Get(enrichment.ReferenceProperty))))
                    .Distinct(StringComparer.Ordinal);
                var resultContexts = targetIds
                    .Where(resultsById.ContainsKey)
                    .ToDictionary(
                        id => id,
                        id => modelService.CreateContext(targetDefinition.Name, resultsById[id]).Values,
                        StringComparer.Ordinal);

                foreach (var enrichment in targetGroup)
                {
                    foreach (var context in enrichment.Contexts)
                    {
                        context.Values[enrichment.ReferenceProperty] =
                            context.Get(enrichment.ReferenceProperty) switch
                            {
                                string reference => resultContexts.GetValueOrDefault(reference),
                                string[] references => references
                                    .Select(resultContexts.GetValueOrDefault)
                                    .Where(result => result != null)
                                    .ToArray(),
                                Dictionary<Lookup, object?> reference => MergeReference(reference, resultContexts),
                                Dictionary<Lookup, object?>[] references => references
                                    .Select(reference => MergeReference(reference, resultContexts)).ToArray(),
                                var value => value
                            };
                    }
                }
            }
        }

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

    /// <summary>
    /// Groups values that belong to the same referenced instance. For example, <c>Course.Name</c> and
    /// <c>Course.Type</c> can be loaded together for each course.
    /// </summary>
    private static IEnumerable<ReferenceEnrichment> GetReferenceEnrichments(EnrichmentState state)
    {
        var workflowDefinition = state.Batch.WorkflowDefinition;
        var referenceProperties = state.Lookups
            .OfType<PropertyLookup>()
            .Distinct()
            .Where(property => property.Parts.Length > 1)
            .Where(property =>
                workflowDefinition.Properties.GetOrDefault(property.Parts[0])?.DataType == DataType.Reference)
            .GroupBy(property => property.Parts[0]);

        foreach (var referenceProperty in referenceProperties)
        {
            var targetDefinition = (workflowDefinition.Properties
                .GetOrDefault(referenceProperty.Key) as ReferencePropertyDefinition)?.WorkflowDefinition;
            if (targetDefinition == null)
                continue;

            yield return new ReferenceEnrichment(
                targetDefinition,
                state.Batch.Contexts,
                referenceProperty.Key,
                referenceProperty.Select(property => property.Parts[1]).Distinct().ToArray());
        }
    }

    /// <summary>Returns the IDs stored in a single reference or a list of references.</summary>
    private static IEnumerable<string> GetReferenceIds(object? value)
        => value switch
        {
            string reference => [reference],
            string[] references => references,
            Dictionary<Lookup, object?> reference when reference.GetValueOrDefault("Id") is string id => [id],
            Dictionary<Lookup, object?>[] references => references.SelectMany(GetReferenceIds),
            _ => []
        };

    private static Dictionary<Lookup, object?> MergeReference(Dictionary<Lookup, object?> reference,
        Dictionary<string, Dictionary<Lookup, object?>> results)
    {
        if (reference.GetValueOrDefault("Id") is string id && results.TryGetValue(id, out var additionalValues))
            foreach (var (key, value) in additionalValues)
                reference.TryAdd(key, value);
        return reference;
    }

    private sealed record EnrichmentState(
        EnrichmentBatch Batch,
        List<Lookup> Lookups,
        bool HasAssessment);

    private sealed record ReferenceEnrichment(
        WorkflowDefinition TargetDefinition,
        ICollection<ObjectContext> Contexts,
        string ReferenceProperty,
        string[] ProjectedProperties);
}