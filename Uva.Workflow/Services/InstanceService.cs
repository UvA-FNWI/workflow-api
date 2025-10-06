using Action = Uva.Workflow.Entities.Domain.Action;

namespace Uva.Workflow.Services;

public class InstanceService(
    WorkflowInstanceService workflowInstanceService,
    IWorkflowInstanceRepository workflowInstanceRepository,
    ModelService modelService,
    RightsService rightsService)
{
    public Task<WorkflowInstance> Get(string instanceId) => workflowInstanceService.GetAsync(instanceId, i => i);

    public Task<List<WorkflowInstance>> GetAll(string entityType) =>
        workflowInstanceService.GetAllAsync(t => t.EntityType == entityType);

    public async Task<Dictionary<string, ObjectContext>> GetProperties(string[] ids, Question[] properties)
    {
        var projection = properties.ToDictionary(p => p.Name, p => $"$Properties.{p.Name}");

        var res = await workflowInstanceService.GetAllByIdAsync(ids, projection);
        return res.ToDictionary(r => r["_id"].ToString()!, r => new ObjectContext(
            properties.ToDictionary(p => (Lookup)p.Name, p => ObjectContext.GetValue(r[p.Name], p))
        ));
    }

    public async Task<List<ObjectContext>> GetScreen(string entityType, Screen screen, string? sourceInstanceId = null)
    {
        var entity = modelService.EntityTypes[entityType];
        var props = screen.Columns.SelectMany(c => c.Properties).ToArray();
        var projection = props
            .Select(p => p.ToString().Split('.')[0])
            .Distinct()
            .ToDictionary(p => p, p => entity.GetKey(p));

        var res = sourceInstanceId != null
            ? await workflowInstanceService.GetAllByParentIdAsync(sourceInstanceId, projection)
            : await workflowInstanceService.GetAllByTypeAsync(entityType, projection);
        return res.ConvertAll(r =>
        {
            var dict = projection.Keys
                .ToDictionary(
                    t => (Lookup)t,
                    t => ObjectContext.GetValue(r.GetValueOrDefault(t), entity.GetDataType(t),
                        entity.Properties.GetValueOrDefault(t))
                );
            dict["Id"] = r["_id"].ToString();
            return new ObjectContext(dict);
        });
    }

    public async Task<bool> CheckLimit(WorkflowInstance instance, Action action)
    {
        if (action.UserProperty == null || action.Limit == null)
            return true;
        var property = action.UserProperty;
        var results = await workflowInstanceService.GetAllByParentIdAsync(instance.Id, new()
        {
            [property] = $"$Properties.{property}"
        });
        var users = results
            .Select(r => r.GetValueOrDefault(property))
            .Where(r => r?.IsBsonNull == false)
            .Select(r => BsonSerializer.Deserialize<User>(r!.AsBsonDocument));
        var userId = await rightsService.GetUserId();
        return users.Count(u => u.Id == userId) < action.Limit.Value;
    }

    public Task<string> GetEntityType(string instanceId)
        => workflowInstanceService.GetAsync(instanceId, i => i.EntityType);

    public async Task UpdateEvent(WorkflowInstance instance, string eventId)
    {
        instance.RecordEvent(eventId);
        await workflowInstanceService.UpdateAsync(instance);
    }

    public async Task<WorkflowInstance> CreateInstance(string entityType, string? userProperty)
    {
        Dictionary<string, BsonValue>? initialProperties = null;

        if (userProperty != null)
        {
            var user = (await rightsService.GetUser()).ToBsonDocument();
            var property = modelService.EntityTypes[entityType].Properties[userProperty];
            initialProperties = new Dictionary<string, BsonValue>
            {
                [userProperty] = property.IsArray ? new BsonArray { user } : user
            };
        }

        return await workflowInstanceService.CreateAsync(entityType, initialProperties: initialProperties);
    }
    public Task SaveValue(WorkflowInstance instance, string? part1, string part2)
        => workflowInstanceRepository.UpdateFieldsAsync(instance.Id,
            Builders<WorkflowInstance>.Update.Set(part1 == null 
                    ? (i => i.Properties[part2]) 
                    : (i => i.Properties[part1][part2]),
                instance.GetProperty(part1, part2)));
 
}