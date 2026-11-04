using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.WorkflowInstances;

public sealed record ImportedStepContext(
    WorkflowInstance Instance,
    WorkflowDefinition Definition,
    Step Step,
    ObjectContext Context);

public class WorkflowStateResolver(
    IWorkflowInstanceRepository repository,
    ModelService modelService,
    WorkflowContextEnricher contextEnricher)
{
    public async Task<ObjectContext> Resolve(WorkflowInstance instance, CancellationToken ct)
    {
        if (!modelService.WorkflowDefinitions[instance.WorkflowDefinition].HasImportedSteps)
            return modelService.CreateContext(instance);
        return (await Resolve([instance], ct))[instance.Id ?? ""];
    }

    public async Task<IReadOnlyDictionary<string, ObjectContext>> Resolve(
        IReadOnlyCollection<WorkflowInstance> instances, CancellationToken ct)
    {
        var contexts = instances.ToDictionary(instance => instance.Id ?? "", modelService.CreateContext);
        var imports = instances.SelectMany(instance =>
                modelService.WorkflowDefinitions[instance.WorkflowDefinition].AllSteps
                    .Where(step => step.Import != null)
                    .Select(step => (Instance: instance, Step: step,
                        SourceId: instance.GetProperty(step.Import!.Reference) as BsonString)))
            .ToArray();
        var ids = imports.Select(import => import.SourceId?.Value)
            .OfType<string>().Where(id => ObjectId.TryParse(id, out _)).Distinct().ToArray();
        var sources = ids.Length == 0
            ? new Dictionary<string, WorkflowInstance>()
            : (await repository.GetByIds(ids, ct)).ToDictionary(source => source.Id);
        var sourceContexts = sources.Values
            .Where(source => modelService.WorkflowDefinitions.ContainsKey(source.WorkflowDefinition))
            .ToDictionary(source => source.Id, modelService.CreateContext);

        var batches = instances.Where(instance =>
                modelService.WorkflowDefinitions[instance.WorkflowDefinition].HasImportedSteps)
            .GroupBy(instance => instance.WorkflowDefinition)
            .Select(group => Batch(group.Key, group.Select(instance => contexts[instance.Id ?? ""]).ToArray()))
            .Concat(sources.Values.GroupBy(source => source.WorkflowDefinition)
                .Where(group => modelService.WorkflowDefinitions.ContainsKey(group.Key))
                .Select(group => Batch(group.Key, group.Select(source => sourceContexts[source.Id]).ToArray())));
        await contextEnricher.Enrich(batches, ct, replaceStep: false);

        foreach (var import in imports)
        {
            var definition = modelService.WorkflowDefinitions[import.Instance.WorkflowDefinition];
            var target = ((ReferencePropertyDefinition)definition.Properties.Get(import.Step.Import!.Reference))
                .WorkflowDefinition!;
            ImportedStepContext? owner = null;
            if (import.SourceId != null && sources.TryGetValue(import.SourceId.Value, out var source) &&
                source.WorkflowDefinition == target.Name)
                owner = new ImportedStepContext(source, target, target.AllSteps.Get(import.Step.Import.Step),
                    sourceContexts[source.Id]);
            contexts[import.Instance.Id ?? ""].ImportedSteps[import.Step.Name] = owner;
        }

        foreach (var instance in instances.Where(instance =>
                     modelService.WorkflowDefinitions[instance.WorkflowDefinition].HasImportedSteps))
        {
            var context = contexts[instance.Id ?? ""];
            context.CurrentStep = modelService.FindOpenStep(instance, context)?.Name;
            context.WorkflowCompletionDate = modelService.GetCompletionDate(instance, context);
        }

        return contexts;

        EnrichmentBatch Batch(string name, ObjectContext[] items)
        {
            var definition = modelService.WorkflowDefinitions[name];
            return new EnrichmentBatch(definition, items,
                definition.Steps.SelectMany(step => step.Lookups).Concat(definition.ProgressLookups)
                    .Concat(definition.AllActions.SelectMany(action => action.Condition?.Properties ?? [])));
        }
    }
}