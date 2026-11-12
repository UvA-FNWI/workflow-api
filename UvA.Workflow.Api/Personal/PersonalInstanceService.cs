using UvA.Workflow.Api.Personal.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Personal;

public class PersonalInstanceService(
    ModelService modelService,
    InstanceService instanceService,
    IWorkflowInstanceRepository workflowInstanceRepository,
    RightsService rightsService)
{
    private static readonly Lookup[] PersonalOverviewLookups =
    [
        new PropertyLookup("Course.Name")
    ];

    public async Task<PersonalInstancesDto> GetInstances(User user, CancellationToken ct)
    {
        if (!ObjectId.TryParse(user.Id, out var userId))
            return new PersonalInstancesDto([], []);

        var userFilter = LinkedUserInstances.BuildFilter(modelService.WorkflowDefinitions.Values, [userId],
            GetVisibleUserProperties);
        if (userFilter == null)
            return new PersonalInstancesDto([], []);

        var instances = await workflowInstanceRepository.GetByFilter(userFilter, ct);
        return await CreateOverview(user, instances, GetVisibleUserProperties, ct);
    }

    public async Task<PersonalInstancesDto> GetPersonInstances(User person, CancellationToken ct)
    {
        if (!ObjectId.TryParse(person.Id, out var personId))
            return new PersonalInstancesDto([], []);

        var filter = LinkedUserInstances.BuildFilter(modelService.WorkflowDefinitions.Values, [personId]);
        if (filter == null)
            return new PersonalInstancesDto([], []);

        var instances = await workflowInstanceRepository.GetByFilter(filter, ct);
        var visibleInstances = new List<WorkflowInstance>();
        foreach (var instance in instances)
        {
            ct.ThrowIfCancellationRequested();
            if (await rightsService.Can(instance, RoleAction.View))
                visibleInstances.Add(instance);
        }

        return await CreateOverview(person, visibleInstances, LinkedUserInstances.GetUserProperties, ct);
    }

    private async Task<PersonalInstancesDto> CreateOverview(
        User user,
        IEnumerable<WorkflowInstance> instances,
        Func<WorkflowDefinition, IEnumerable<PropertyDefinition>> getLinkedProperties,
        CancellationToken ct)
    {
        var instanceContexts = instances
            .Select(instance => new PersonalInstanceContext(instance, modelService.CreateContext(instance)))
            .ToArray();

        await Enrich(instanceContexts, ct);

        var instanceDtos = instanceContexts
            .Select(item => CreateDto(item.Instance, item.Context, user, getLinkedProperties))
            .Where(dto => dto.Roles.Length > 0)
            .OrderByDescending(dto => dto.CreatedOn)
            .ThenByDescending(dto => dto.Id)
            .ToArray();

        var roleLookup = instanceDtos
            .SelectMany(dto => dto.Roles.Select(name => (dto.WorkflowDefinition, name)))
            .GroupBy(x => x.name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => modelService.WorkflowDefinitions[x.WorkflowDefinition].Roles.GetOrDefault(x.name))
                    .FirstOrDefault(r => r != null) ?? new Role { Name = g.Key },
                StringComparer.OrdinalIgnoreCase);

        var roles = instanceDtos
            .SelectMany(instance => instance.Roles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => roleLookup[name])
            .OrderBy(role => role.Order)
            .ThenBy(role => role.Name, StringComparer.OrdinalIgnoreCase)
            .Select(role => new PersonalRoleDto(role.Name, role.DisplayTitle))
            .ToArray();

        return new PersonalInstancesDto(roles, instanceDtos);
    }

    private PersonalInstanceDto CreateDto(
        WorkflowInstance instance,
        ObjectContext context,
        User user,
        Func<WorkflowDefinition, IEnumerable<PropertyDefinition>> getLinkedProperties)
    {
        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var usersByRole = LinkedUserInstances.GetUserProperties(definition)
            .ToDictionary(
                property => property.Name,
                property => GetUsers(context.Get(property.Name)).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var roles = getLinkedProperties(definition)
            .Where(property => usersByRole[property.Name].Any(instanceUser =>
                string.Equals(instanceUser.Id, user.Id, StringComparison.Ordinal)))
            .Select(property => property.Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var student = usersByRole.GetValueOrDefault("Student")
            ?.FirstOrDefault()
            ?.DisplayName;
        var employees = usersByRole
            .Where(entry => !string.Equals(entry.Key, "Student", StringComparison.OrdinalIgnoreCase))
            .SelectMany(entry => entry.Value)
            .Where(instanceUser => !string.Equals(instanceUser.Id, user.Id, StringComparison.Ordinal))
            .Select(instanceUser => instanceUser.DisplayName)
            .Where(displayName => !string.IsNullOrWhiteSpace(displayName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PersonalInstanceDto(
            instance.Id,
            instance.WorkflowDefinition,
            definition.DisplayTitle,
            definition.InstanceTitleTemplate?.Apply(context),
            instance.CurrentStep,
            StepProgressResolver.ResolveProgress(definition, instance.CurrentStep, context),
            instance.CreatedOn,
            roles,
            student,
            context.Get("Course.Name") as string,
            employees
        );
    }

    private async Task Enrich(
        IEnumerable<PersonalInstanceContext> instanceContexts,
        CancellationToken ct)
    {
        var batches = instanceContexts
            .GroupBy(item => item.Instance.WorkflowDefinition)
            .Select(group =>
            {
                var definition = modelService.WorkflowDefinitions[group.Key];
                var lookups = definition.ProgressLookups
                    .Concat(PersonalOverviewLookups)
                    .Distinct()
                    .ToArray();
                return new EnrichmentBatch(
                    definition,
                    group.Select(item => item.Context).ToArray(),
                    lookups);
            })
            .ToArray();

        await instanceService.Enrich(batches, ct, replaceStep: false);
    }

    private static IEnumerable<PropertyDefinition> GetVisibleUserProperties(WorkflowDefinition definition)
    {
        var rolesWithViewAccess = definition.GlobalActions
            .Where(action => action.Type == RoleAction.View)
            .SelectMany(action => action.Roles)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return LinkedUserInstances.GetUserProperties(definition)
            .Where(property => rolesWithViewAccess.Contains(property.Name));
    }

    private static IEnumerable<InstanceUser> GetUsers(object? value)
        => value switch
        {
            InstanceUser user => [user],
            InstanceUser[] users => users,
            _ => []
        };

    private sealed record PersonalInstanceContext(WorkflowInstance Instance, ObjectContext Context);
}