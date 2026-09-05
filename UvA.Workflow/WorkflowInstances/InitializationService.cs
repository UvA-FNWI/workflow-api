using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.WorkflowInstances;

public class InitializationService(
    ModelService modelService,
    IWorkflowInstanceRepository instanceRepository,
    WorkflowInstanceService workflowInstanceService)
{
    public async Task CreateSeedData(CancellationToken ct = default)
    {
        foreach (var definition in modelService.WorkflowDefinitions.Values.Where(d => d.SeedData != null))
        {
            var current = await instanceRepository.GetAllByType(
                definition.Name,
                new Dictionary<string, string> { ["ExternalId"] = "$Properties.ExternalId" },
                ct
            );
            var currentIds = current.Select(i => i.GetValueOrDefault("ExternalId")?.ToString()).ToHashSet();

            foreach (var row in definition.SeedData!)
            {
                if (!row.TryGetValue("ExternalId", out var externalId))
                    throw new Exception("Seed data must contain an ExternalId");
                if (currentIds.Contains(externalId))
                    continue;
                await workflowInstanceService.Create(definition.Name, null, ct,
                    initialProperties: row.ToDictionary(k => k.Key, k => (BsonValue)new BsonString(k.Value)));
            }
        }
    }
}