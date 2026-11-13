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
        var pendingBatches = batches.ToArray();
        var nestedContexts = new List<ObjectContext>();
        while (pendingBatches.Length > 0)
        {
            var targetGroups = pendingBatches
                .SelectMany(GetReferenceEnrichments)
                .GroupBy(enrichment => enrichment.TargetDefinition.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var referenceIds = targetGroups
                .SelectMany(group => group)
                .SelectMany(enrichment => enrichment.Contexts.SelectMany(context =>
                    GetReferenceIds(GetReferenceValue(context, enrichment))))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var resultsById = new Dictionary<string, Dictionary<string, BsonValue>>();
            if (referenceIds.Length > 0)
            {
                var projection = targetGroups
                    .SelectMany(group => group.SelectMany(enrichment => enrichment.Projection))
                    .DistinctBy(entry => entry.Key, StringComparer.Ordinal)
                    .ToDictionary(entry => entry.Key, entry => entry.Value);
                var results = await workflowInstanceRepository.GetAllById(referenceIds, projection, ct);
                resultsById = results.ToDictionary(result => result["_id"].ToString()!);
            }

            var nextBatches = new List<EnrichmentBatch>();
            foreach (var targetGroup in targetGroups)
            {
                var targetDefinition = targetGroup.First().TargetDefinition;
                var targetIds = targetGroup
                    .SelectMany(enrichment => enrichment.Contexts.SelectMany(context =>
                        GetReferenceIds(GetReferenceValue(context, enrichment))))
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
                        var source = GetReferenceValue(context, enrichment);
                        var loaded = source switch
                        {
                            string reference => resultContexts.GetValueOrDefault(reference),
                            string[] references => references
                                .Select(resultContexts.GetValueOrDefault)
                                .Where(result => result != null)
                                .ToArray(),
                            var value => value
                        };
                        if (GetReferenceIds(source).Any())
                        {
                            if (GetReferenceIds(source).Any(id => !resultsById.ContainsKey(id)))
                                context.UnresolvedReferences.Add(enrichment.ReferenceProperty);
                            else
                                context.UnresolvedReferences.Remove(enrichment.ReferenceProperty);
                        }

                        context.LoadedReferenceValues[enrichment.ReferenceProperty] = loaded;
                    }
                }

                var remainingLookups = targetGroup.SelectMany(enrichment => enrichment.Lookups)
                    .Where(lookup => lookup is PropertyLookup { Parts.Length: > 1 })
                    .Distinct()
                    .ToArray();
                if (remainingLookups.Length == 0)
                    continue;

                var loadedContexts = targetGroup
                    .SelectMany(enrichment => enrichment.Contexts.SelectMany(context =>
                        GetLoadedReferences(
                            context.LoadedReferenceValues.GetValueOrDefault(enrichment.ReferenceProperty))))
                    .Distinct()
                    .Select(values => new ObjectContext(values))
                    .ToArray();
                if (loadedContexts.Length > 0)
                {
                    nestedContexts.AddRange(loadedContexts);
                    nextBatches.Add(new EnrichmentBatch(targetDefinition, loadedContexts, remainingLookups));
                }
            }

            pendingBatches = nextBatches.ToArray();
        }

        // Nested dictionaries are traversal views; keep the source context's reference IDs intact.
        foreach (var context in nestedContexts)
        foreach (var (key, value) in context.LoadedReferenceValues)
            context.Values[key] = value;
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
            foreach (var lookup in lookups.OfType<PropertyLookup>().Where(lookup =>
                         targetDefinition.Properties.GetOrDefault(lookup.Parts[0])?.IsComputed == true))
                targetDefinition.ValidateReadLookups([lookup], allowReference: false);
            yield return new ReferenceRequest(targetDefinition, batch.Contexts, referenceProperty.Key,
                lookups, targetDefinition.GetProjection(lookups));
        }
    }

    private static object? GetReferenceValue(ObjectContext context, ReferenceRequest request)
    {
        if (context.LoadedReferenceValues.TryGetValue(request.ReferenceProperty, out var loaded)
            && GetLoadedReferences(loaded).All(values => request.Projection.Keys.All(key =>
                values.ContainsKey(key == "_id" ? "Id" : key))))
            return loaded;
        return context.Get(request.ReferenceProperty);
    }

    /// <summary>Returns the IDs stored in a single reference or a list of references.</summary>
    private static IEnumerable<string> GetReferenceIds(object? value)
        => value switch
        {
            string reference => [reference],
            string[] references => references,
            _ => []
        };

    private static IEnumerable<Dictionary<Lookup, object?>> GetLoadedReferences(object? value)
        => value switch
        {
            Dictionary<Lookup, object?> values => [values],
            Dictionary<Lookup, object?>[] values => values,
            _ => []
        };

    private sealed record ReferenceRequest(
        WorkflowDefinition TargetDefinition,
        ICollection<ObjectContext> Contexts,
        string ReferenceProperty,
        Lookup[] Lookups,
        Dictionary<string, string> Projection);
}