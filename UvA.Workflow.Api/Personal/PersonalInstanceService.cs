using UvA.Workflow.Api.Personal.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Personal;

public class PersonalInstanceService(
    ModelService modelService,
    IWorkflowInstanceRepository workflowInstanceRepository)
{
    public async Task<PersonalInstancesDto> GetInstances(User user, CancellationToken ct)
    {
        if (!ObjectId.TryParse(user.Id, out var userId))
            return new PersonalInstancesDto([], []);

        var definitions = modelService.WorkflowDefinitions.Values
            .Where(definition => GetUserProperties(definition).Any())
            .ToArray();

        if (definitions.Length == 0)
            return new PersonalInstancesDto([], []);

        var userFilter = BuildUserFilter(definitions, userId);
        var instances = (await workflowInstanceRepository.GetByFilter(userFilter, ct)).ToArray();
        var courseNames = await GetCourseNames(instances, ct);

        var instanceDtos = instances
            .Select(instance => CreateDto(instance, user, courseNames))
            .Where(dto => dto.Roles.Length > 0)
            .OrderByDescending(dto => dto.CreatedOn)
            .ThenByDescending(dto => dto.Id)
            .ToArray();

        var roles = instanceDtos
            .SelectMany(instance => instance.Roles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(role => new PersonalRoleDto(
                role,
                modelService.Roles.GetValueOrDefault(role)?.DisplayTitle ?? role))
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
            var userPropertyFilters = GetUserProperties(definition)
                .Select(property => filterBuilder.Eq($"Properties.{property.Name}._id", userId));

            return filterBuilder.And(
                filterBuilder.Eq(instance => instance.WorkflowDefinition, definition.Name),
                filterBuilder.Or(userPropertyFilters));
        });

        return filterBuilder.Or(definitionFilters);
    }

    private PersonalInstanceDto CreateDto(
        WorkflowInstance instance,
        User user,
        IReadOnlyDictionary<string, string> courseNames)
    {
        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var context = modelService.CreateContext(instance);
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
            GetCourseName(instance.Properties.GetValueOrDefault("Course"), courseNames),
            employees
        );
    }

    private static IEnumerable<PropertyDefinition> GetUserProperties(WorkflowDefinition definition)
        => definition.Properties
            .Where(property => property.DataType == DataType.User)
            .DistinctBy(property => property.Name, StringComparer.OrdinalIgnoreCase);

    private async Task<IReadOnlyDictionary<string, string>> GetCourseNames(
        IEnumerable<WorkflowInstance> instances,
        CancellationToken ct)
    {
        var ids = instances
            .Select(instance => GetReferenceId(instance.Properties.GetValueOrDefault("Course")))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0)
            return new Dictionary<string, string>();

        var courses = await workflowInstanceRepository.GetAllById(ids, new Dictionary<string, string>
        {
            ["Name"] = "$Properties.Name"
        }, ct);

        return courses
            .Select(course => new
            {
                Id = GetReferenceId(course.GetValueOrDefault("_id")),
                Name = course.GetValueOrDefault("Name") is BsonString name ? name.Value : null
            })
            .Where(course => course.Id != null && !string.IsNullOrWhiteSpace(course.Name))
            .ToDictionary(course => course.Id!, course => course.Name!, StringComparer.Ordinal);
    }

    private static string? GetCourseName(
        BsonValue? value,
        IReadOnlyDictionary<string, string> courseNames)
    {
        if (value is BsonDocument course &&
            course.GetValue("Name", BsonNull.Value) is BsonString embeddedName)
            return embeddedName.Value;

        var id = GetReferenceId(value);
        return id != null ? courseNames.GetValueOrDefault(id) : null;
    }

    private static string? GetReferenceId(BsonValue? value)
        => value switch
        {
            BsonObjectId objectId => objectId.Value.ToString(),
            BsonString text => text.Value,
            _ => null
        };

    private static IEnumerable<InstanceUser> GetUsers(object? value)
        => value switch
        {
            InstanceUser user => [user],
            InstanceUser[] users => users,
            _ => []
        };
}