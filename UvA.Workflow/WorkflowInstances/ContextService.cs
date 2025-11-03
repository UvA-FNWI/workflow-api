namespace UvA.Workflow.Services;

public class ContextService(
    ModelService modelService,
    InstanceService instanceService,
    IWorkflowInstanceRepository workflowInstanceRepository)
{
    public async Task UpdateCurrentStep(WorkflowInstance instance, CancellationToken ct)
    {
        var entityType = modelService.EntityTypes[instance.EntityType];
        var context = modelService.CreateContext(instance);
        await Enrich(entityType, [context], entityType.Steps.SelectMany(s => s.Lookups), ct);
        string? targetStep = null;
        foreach (var step in entityType.Steps)
        {
            if (step.Condition.IsMet(context) && !step.HasEnded(context))
            {
                targetStep = step.Name;
                break;
            }
        }

        if (instance.CurrentStep != targetStep)
        {
            instance.CurrentStep = targetStep;
            if (!string.IsNullOrEmpty(instance.Id))
                await workflowInstanceRepository.UpdateField(instance.Id, i => i.CurrentStep, targetStep ?? "", ct);
        }
    }

    public async Task Enrich(EntityType entityType, ICollection<ObjectContext> contexts, IEnumerable<Lookup> properties,
        CancellationToken ct)
    {
        var groups = properties
            .Where(p => p is PropertyLookup)
            .Cast<PropertyLookup>()
            .Distinct()
            .Where(p => p.Parts.Length > 1)
            .Where(p => entityType.Properties[p.Parts[0]].DataType == DataType.Reference)
            .GroupBy(p => p.Parts[0])
            .ToArray();

        foreach (var referenceGroup in groups)
        {
            var ids = contexts.ToDictionary(c => c, c => c.Get(referenceGroup.Key) as string);
            var targetType = entityType.Properties[referenceGroup.Key].EntityType!;
            var props = referenceGroup.Select(p => targetType.Properties[p.Parts[1]]).ToArray();
            var results = await instanceService.GetProperties(ids.Values.Where(i => i != null).ToArray()!, props, ct);
            foreach (var context in contexts)
            {
                var id = ids[context];
                var result = results.GetValueOrDefault(id ?? "");
                if (result == null)
                    continue;
                foreach (var reference in referenceGroup)
                    if (result.Values.TryGetValue(reference.Parts[1], out var value))
                        context.Values[reference] = value;
            }
        }

        foreach (var context in contexts)
        {
            if (context.Values.TryGetValue("CurrentStep", out var id) && id is string stepName)
                context.Values["CurrentStep"] = entityType.AllSteps[stepName].DisplayTitle;
        }
    }
}