using UvA.Workflow.Api.Personal.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Personal;

public class PersonalInstanceService(
    ModelService modelService,
    IWorkflowInstanceRepository workflowInstanceRepository)
{
    public async Task<PersonalInstanceDto[]> GetInstances(User user, CancellationToken ct)
    {
        if (!ObjectId.TryParse(user.Id, out var userId))
            return [];

        var directBindings = modelService.RoleBindings.All
            .Where(binding => binding.Source == RoleBindingSource.Direct)
            .ToArray();

        if (directBindings.Length == 0)
            return [];

        var userFilter = BuildUserFilter(directBindings, userId);
        var instances = await workflowInstanceRepository.GetByFilter(userFilter, ct);

        return instances
            .Select(instance => CreateDto(instance, userId))
            .Where(dto => dto.Roles.Length > 0)
            .OrderByDescending(dto => dto.CreatedOn)
            .ThenByDescending(dto => dto.Id)
            .ToArray();
    }

    private static FilterDefinition<WorkflowInstance> BuildUserFilter(
        IEnumerable<RoleBinding> bindings,
        ObjectId userId)
    {
        var filterBuilder = Builders<WorkflowInstance>.Filter;
        var bindingFilters = bindings.Select(binding =>
            filterBuilder.And(
                filterBuilder.Eq(instance => instance.WorkflowDefinition, binding.WorkflowDefinition),
                filterBuilder.Eq(binding.UserIdPath!, userId)
            ));

        return filterBuilder.Or(bindingFilters);
    }

    private PersonalInstanceDto CreateDto(WorkflowInstance instance, ObjectId userId)
    {
        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var roles = modelService.RoleBindings.GetBindings(instance.WorkflowDefinition)
            .Where(binding => binding.Source == RoleBindingSource.Direct)
            .Where(binding => ContainsUser(instance.Properties.GetValueOrDefault(binding.PropertyName), userId))
            .Select(binding => binding.Role)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PersonalInstanceDto(
            instance.Id,
            instance.WorkflowDefinition,
            definition.DisplayTitle,
            definition.InstanceTitleTemplate?.Apply(modelService.CreateContext(instance)),
            instance.CurrentStep,
            instance.CreatedOn,
            roles
        );
    }

    private static bool ContainsUser(BsonValue? value, ObjectId userId)
        => value switch
        {
            BsonDocument user => HasUserId(user, userId),
            BsonArray users => users.Any(candidate =>
                candidate is BsonDocument user && HasUserId(user, userId)),
            _ => false
        };

    private static bool HasUserId(BsonDocument user, ObjectId userId)
    {
        if (!user.TryGetValue("_id", out var storedUserId))
            return false;

        return storedUserId switch
        {
            BsonObjectId objectId => objectId.Value == userId,
            BsonString text => ObjectId.TryParse(text.Value, out var parsedUserId) && parsedUserId == userId,
            _ => false
        };
    }
}