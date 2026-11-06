using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.WorkflowInstances;

internal static class ContextLoader
{
    public static async Task<ObjectContext> CreateContext(ModelService modelService,
        IWorkflowInstanceRepository repository, WorkflowInstance instance, IEnumerable<Lookup> lookups,
        CancellationToken ct)
    {
        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var context = modelService.CreateContext(instance);
        await LoadReferences(modelService, repository,
            [new EnrichmentBatch(definition, [context], lookups)], ct);
        context.ComputeProperties(definition);
        return context;
    }

    public static async Task LoadReferences(ModelService modelService,
        IWorkflowInstanceRepository workflowInstanceRepository, IEnumerable<EnrichmentBatch> batches,
        CancellationToken ct)
    {
        var referenceEnrichments = batches
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
            var projection = targetGroups
                .SelectMany(targetGroup => targetGroup.SelectMany(enrichment => enrichment.Projection))
                .DistinctBy(entry => entry.Key, StringComparer.Ordinal)
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            var results = await workflowInstanceRepository.GetAllById(
                referenceIds,
                projection,
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
                        id => modelService.CreateContext(targetDefinition.Name, resultsById[id],
                            targetGroup.SelectMany(enrichment => enrichment.Projection.Keys)).Values,
                        StringComparer.Ordinal);

                foreach (var enrichment in targetGroup)
                {
                    foreach (var context in enrichment.Contexts)
                    {
                        var loaded = context.Get(enrichment.ReferenceProperty) switch
                        {
                            string reference => resultContexts.GetValueOrDefault(reference),
                            string[] references => references
                                .Select(resultContexts.GetValueOrDefault)
                                .Where(result => result != null)
                                .ToArray(),
                            var value => value
                        };
                        if (GetReferenceIds(context.Get(enrichment.ReferenceProperty))
                            .Any(id => !resultsById.ContainsKey(id)))
                            context.UnresolvedReferences.Add(enrichment.ReferenceProperty);
                        else
                            context.UnresolvedReferences.Remove(enrichment.ReferenceProperty);
                        context.LoadedReferenceValues[enrichment.ReferenceProperty] = loaded;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Groups values that belong to the same referenced instance. For example, <c>Course.Name</c> and
    /// <c>Course.Type</c> can be loaded together for each course.
    /// </summary>
    private static IEnumerable<ReferenceRequest> GetReferenceEnrichments(EnrichmentBatch batch)
    {
        var workflowDefinition = batch.WorkflowDefinition;
        var referenceProperties = workflowDefinition.GetSourceLookups(batch.Lookups)
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

            var lookups = referenceProperty.Select(property => (Lookup)string.Join('.', property.Parts.Skip(1)))
                .ToArray();
            targetDefinition.ValidateReadLookups(lookups, allowReference: false);
            yield return new ReferenceRequest(targetDefinition, batch.Contexts, referenceProperty.Key,
                targetDefinition.GetProjection(lookups));
        }
    }

    /// <summary>Returns the IDs stored in a single reference or a list of references.</summary>
    private static IEnumerable<string> GetReferenceIds(object? value)
        => value switch
        {
            string reference => [reference],
            string[] references => references,
            _ => []
        };

    private sealed record ReferenceRequest(
        WorkflowDefinition TargetDefinition,
        ICollection<ObjectContext> Contexts,
        string ReferenceProperty,
        Dictionary<string, string> Projection);
}