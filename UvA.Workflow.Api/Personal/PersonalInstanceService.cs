using UvA.Workflow.Api.Personal.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
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
        var instances = (await workflowInstanceRepository.GetByFilter(userFilter, ct)).ToArray();
        var courseNames = await GetCourseNames(instances, ct);

        return instances
            .Select(instance => CreateDto(instance, userId, courseNames))
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

    private PersonalInstanceDto CreateDto(
        WorkflowInstance instance,
        ObjectId userId,
        IReadOnlyDictionary<string, string> courseNames)
    {
        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var context = modelService.CreateContext(instance);
        var directBindings = modelService.RoleBindings.GetBindings(instance.WorkflowDefinition)
            .Where(binding => binding.Source == RoleBindingSource.Direct)
            .ToArray();
        var roles = directBindings
            .Where(binding => ContainsUser(instance.Properties.GetValueOrDefault(binding.PropertyName), userId))
            .Select(binding => binding.Role)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var student = directBindings
            .Where(binding => string.Equals(binding.Role, "Student", StringComparison.OrdinalIgnoreCase))
            .SelectMany(binding => GetUserDisplayNames(
                instance.Properties.GetValueOrDefault(binding.PropertyName)))
            .FirstOrDefault();
        var employees = directBindings
            .Where(binding => !string.Equals(binding.Role, "Student", StringComparison.OrdinalIgnoreCase))
            .SelectMany(binding => GetUserDisplayNames(
                instance.Properties.GetValueOrDefault(binding.PropertyName)))
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

    private static bool ContainsUser(BsonValue? value, ObjectId userId)
        => GetUsers(value).Any(user => HasUserId(user, userId));

    private static IEnumerable<string> GetUserDisplayNames(BsonValue? value)
        => GetUsers(value)
            .Select(user => user.GetValue("DisplayName", BsonNull.Value))
            .OfType<BsonString>()
            .Select(displayName => displayName.Value)
            .Where(displayName => !string.IsNullOrWhiteSpace(displayName));

    private static IEnumerable<BsonDocument> GetUsers(BsonValue? value)
    {
        if (value is BsonDocument user)
        {
            yield return user;
            yield break;
        }

        if (value is not BsonArray users)
            yield break;

        foreach (var candidate in users)
            if (candidate is BsonDocument arrayUser)
                yield return arrayUser;
    }

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