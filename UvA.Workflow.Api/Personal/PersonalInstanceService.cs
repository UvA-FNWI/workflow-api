using UvA.Workflow.Api.Personal.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Personal;

public class PersonalInstanceService(
    ModelService modelService,
    InstanceService instanceService,
    IWorkflowInstanceRepository workflowInstanceRepository)
{
    private static readonly Lookup[] PersonalOverviewLookups =
    [
        new PropertyLookup("Course.Name")
    ];

    public async Task<PersonalInstancesDto> GetInstances(User user, CancellationToken ct)
    {
        if (!ObjectId.TryParse(user.Id, out var userId))
            return new PersonalInstancesDto([], []);

        var definitions = modelService.WorkflowDefinitions.Values
            .Where(definition => GetVisibleUserProperties(definition).Any())
            .ToArray();

        if (definitions.Length == 0)
            return new PersonalInstancesDto([], []);

        var userFilter = BuildUserFilter(definitions, userId);
        var instances = (await workflowInstanceRepository.GetByFilter(userFilter, ct)).ToArray();
        var instanceContexts = instances
            .Select(instance => new PersonalInstanceContext(instance, modelService.CreateContext(instance)))
            .ToArray();

        await Enrich(instanceContexts, ct);

        var instanceDtos = instanceContexts
            .Select(item => CreateDto(item.Instance, item.Context, user))
            .Where(dto => dto.Roles.Length > 0)
            .OrderByDescending(dto => dto.CreatedOn)
            .ThenByDescending(dto => dto.Id)
            .ToArray();

        var roles = instanceDtos
            .SelectMany(instance => instance.Roles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => modelService.Roles.GetValueOrDefault(name) ?? new Role { Name = name })
            .OrderBy(role => role.Order)
            .ThenBy(role => role.Name, StringComparer.OrdinalIgnoreCase)
            .Select(role => new PersonalRoleDto(role.Name, role.DisplayTitle))
            .ToArray();

        return new PersonalInstancesDto(roles, instanceDtos);
    }

    private static FilterDefinition<WorkflowInstance> BuildUserFilter(
        IEnumerable<WorkflowDefinition> definitions,
        ObjectId userId)
    {
        var filterBuilder = Builders<WorkflowInstance>.Filter;
        var definitionFilters = definitions.Select(definition =>
        {
            var userPropertyFilters = GetVisibleUserProperties(definition)
                .Select(property => filterBuilder.Eq($"Properties.{property.Name}._id", userId));

            return filterBuilder.And(
                filterBuilder.Eq(instance => instance.WorkflowDefinition, definition.Name),
                filterBuilder.Or(userPropertyFilters));
        });

        return filterBuilder.Or(definitionFilters);
    }

    private PersonalInstanceDto CreateDto(
        WorkflowInstance instance,
        ObjectContext context,
        User user)
    {
        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var usersByRole = GetUserProperties(definition)
            .ToDictionary(
                property => property.Name,
                property => GetUsers(context.Get(property.Name)).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var roles = usersByRole
            .Where(entry => entry.Value.Any(instanceUser =>
                string.Equals(instanceUser.Id, user.Id, StringComparison.Ordinal)))
            .Select(entry => entry.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
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
            ProgressInformationDto.Resolve(definition, instance.CurrentStep, context),
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

    private static IEnumerable<PropertyDefinition> GetUserProperties(WorkflowDefinition definition)
        => definition.Properties
            .Where(property => property.DataType == DataType.User)
            .DistinctBy(property => property.Name, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<PropertyDefinition> GetVisibleUserProperties(WorkflowDefinition definition)
    {
        var rolesWithViewAccess = definition.GlobalActions
            .Where(action => action.Type == RoleAction.View)
            .SelectMany(action => action.Roles)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return GetUserProperties(definition)
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