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

        var pendingBatches = states
            .Select(state => state.Batch with { Lookups = state.Lookups })
            .ToArray();

        while (pendingBatches.Length > 0)
        {
            var targetGroups = pendingBatches
                .SelectMany(GetReferenceEnrichments)
                .GroupBy(enrichment => enrichment.TargetDefinition.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var referenceIds = targetGroups
                .SelectMany(group => group)
                .SelectMany(enrichment => enrichment.Contexts.SelectMany(context =>
                    GetReferenceIds(context.Get(enrichment.ReferenceProperty), enrichment.ProjectedProperties)))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var resultsById = new Dictionary<string, Dictionary<string, BsonValue>>();

            if (referenceIds.Length > 0)
            {
                var projection = targetGroups
                    .SelectMany(group => group)
                    .SelectMany(enrichment => enrichment.ProjectedProperties)
                    .Select(property => property.Split('.')[0])
                    .Distinct(StringComparer.Ordinal)
                    .ToDictionary(property => property, property => $"$Properties.{property}");

                var results = await workflowInstanceRepository.GetAllById(
                    referenceIds, projection, ct);

                resultsById = results.ToDictionary(result => result["_id"].ToString()!);
            }

            var nextBatches = new List<EnrichmentBatch>();

            foreach (var targetGroup in targetGroups)
            {
                var targetDefinition = targetGroup.First().TargetDefinition;

                var targetIds = targetGroup
                    .SelectMany(enrichment => enrichment.Contexts.SelectMany(context =>
                        GetReferenceIds(context.Get(enrichment.ReferenceProperty), enrichment.ProjectedProperties)))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

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
                                    .OfType<Dictionary<Lookup, object?>>()
                                    .ToArray(),
                                Dictionary<Lookup, object?> reference => MergeReference(reference, resultContexts),
                                Dictionary<Lookup, object?>[] references => references
                                    .Select(reference => MergeReference(reference, resultContexts)).ToArray(),
                                var value => value
                            };
                    }
                }

                var remainingLookups = targetGroup
                    .SelectMany(enrichment => enrichment.ProjectedProperties)
                    .Where(property => property.Contains('.'))
                    .Select(property => new PropertyLookup(property))
                    .Distinct()
                    .ToArray();

                if (remainingLookups.Length == 0)
                    continue;

                // Both newly loaded and previously loaded references are dictionaries now.
                var loadedContexts = targetGroup
                    .SelectMany(enrichment => enrichment.Contexts.SelectMany(context =>
                        GetLoadedReferences(context.Get(enrichment.ReferenceProperty))))
                    .Distinct()
                    .Select(values => new ObjectContext(values))
                    .ToArray();

                if (loadedContexts.Length > 0)
                {
                    nextBatches.Add(new EnrichmentBatch(
                        targetDefinition,
                        loadedContexts,
                        remainingLookups));
                }
            }

            pendingBatches = nextBatches.ToArray();
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
    private static IEnumerable<ReferenceEnrichment> GetReferenceEnrichments(EnrichmentBatch batch)
    {
        var workflowDefinition = batch.WorkflowDefinition;
        var referenceProperties = batch.Lookups
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
                batch.Contexts,
                referenceProperty.Key,
                referenceProperty.Select(property => string.Join('.', property.Parts.Skip(1))).Distinct().ToArray());
        }
    }

    /// <summary>Returns the IDs stored in a single reference or a list of references.</summary>
    private static IEnumerable<string> GetReferenceIds(object? value, string[] projectedProperties)
        => value switch
        {
            string reference => [reference],
            string[] references => references,
            Dictionary<Lookup, object?> reference when
                projectedProperties.Any(property => !reference.ContainsKey(property.Split('.')[0])) &&
                reference.GetValueOrDefault("Id") is string id => [id],
            Dictionary<Lookup, object?>[] references => references.SelectMany(reference =>
                GetReferenceIds(reference, projectedProperties)),
            _ => []
        };

    private static IEnumerable<Dictionary<Lookup, object?>> GetLoadedReferences(object? value)
        => value switch
        {
            Dictionary<Lookup, object?> values => [values],
            Dictionary<Lookup, object?>[] values => values,
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