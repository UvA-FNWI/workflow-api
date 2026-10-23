using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Authentication.CanvasLti;

public class CanvasLaunchTargetResolver(
    IWorkflowInstanceRepository workflowInstanceRepository,
    ModelService modelService,
    IOptions<CanvasLtiOptions> options,
    ILogger<CanvasLaunchTargetResolver> logger)
{
    private readonly CanvasLtiOptions options = options.Value;

    public async Task<string> ResolveTarget(User user, CanvasLaunchInfo launchInfo, CancellationToken ct)
    {
        if (launchInfo.IsTeacher)
            return await ResolveTeacherTarget(launchInfo, ct);

        var candidates = await GetStudentInstances(user.Id, ct);
        if (candidates.Count == 0)
        {
            logger.LogWarning("No workflow instances found for Canvas student {UvanetId}", launchInfo.UvanetId);
            return options.FallbackTarget;
        }

        var matchingContextIds = await FindMatchingContextIds(candidates, launchInfo.CourseIdentifiers, ct);
        var selected = candidates
                           .Where(instance => GetContextProperties(instance.WorkflowDefinition)
                               .SelectMany(propertyName =>
                                   GetReferenceIds(instance.GetProperty(propertyName)))
                               .Any(matchingContextIds.Contains))
                           .OrderByDescending(instance => instance.CreatedOn)
                           .FirstOrDefault()
                       ?? candidates.OrderByDescending(instance => instance.CreatedOn).First();

        if (candidates.Count > 1)
        {
            logger.LogInformation(
                "Resolved Canvas student {UvanetId} to workflow instance {InstanceId} from {CandidateCount} candidates",
                launchInfo.UvanetId,
                selected.Id,
                candidates.Count);
        }

        return $"/instance/{selected.Id}";
    }

    private async Task<string> ResolveTeacherTarget(CanvasLaunchInfo launchInfo, CancellationToken ct)
    {
        var instances = await GetContextInstances(launchInfo.CourseIdentifiers, ct);
        var definitions = instances
            .Select(i => i.WorkflowDefinition)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => modelService.WorkflowDefinitions.GetValueOrDefault(name))
            .Where(definition => definition?.Screens is { Count: > 0 })
            .OrderBy(definition => definition!.Name, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();

        if (definitions.Length != 1)
            return options.TeacherTarget;

        var definition = definitions[0]!;
        var target = $"/screens/{definition.Name}/{definition.Screens[0].Name}";
        logger.LogInformation(
            "Resolved Canvas teacher {UvanetId} to workflow definition {WorkflowDefinition}",
            launchInfo.UvanetId,
            definition.Name);
        return target;
    }

    private async Task<List<WorkflowInstance>> GetContextInstances(
        IReadOnlyCollection<string> launchCourseIdentifiers,
        CancellationToken ct)
    {
        if (launchCourseIdentifiers.Count == 0)
            return [];

        var courseFilter = Builders<WorkflowInstance>.Filter.In(
            "Properties.ExternalId",
            launchCourseIdentifiers);
        var contexts = await workflowInstanceRepository.GetByWorkflowDefinition("Context", courseFilter, ct);
        var contextIds = contexts
            .Select(course => course.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (contextIds.Length == 0)
            return [];

        var definitionFilters = modelService.WorkflowDefinitions.Values
            .Select(definition =>
            {
                var propertyFilters = GetContextProperties(definition.Name)
                    .Select(property => Builders<WorkflowInstance>.Filter.In(
                        $"Properties.{property}",
                        contextIds))
                    .ToArray();

                if (propertyFilters.Length == 0)
                    return null;

                return Builders<WorkflowInstance>.Filter.And(
                    Builders<WorkflowInstance>.Filter.Eq(
                        instance => instance.WorkflowDefinition,
                        definition.Name),
                    Builders<WorkflowInstance>.Filter.Or(propertyFilters));
            })
            .OfType<FilterDefinition<WorkflowInstance>>()
            .ToArray();

        if (definitionFilters.Length == 0)
            return [];

        var instanceFilter = Builders<WorkflowInstance>.Filter.Or(definitionFilters);
        return (await workflowInstanceRepository.GetByFilter(instanceFilter, ct)).ToList();
    }

    private async Task<List<WorkflowInstance>> GetStudentInstances(string userId, CancellationToken ct)
    {
        if (!ObjectId.TryParse(userId, out var objectId))
            return [];

        var filter = Builders<WorkflowInstance>.Filter.Eq("Properties.Student._id", objectId);
        return (await workflowInstanceRepository.GetByFilter(filter, ct)).ToList();
    }

    private async Task<HashSet<string>> FindMatchingContextIds(
        IEnumerable<WorkflowInstance> candidates,
        IReadOnlyCollection<string> launchCourseIdentifiers,
        CancellationToken ct)
    {
        if (launchCourseIdentifiers.Count == 0)
            return [];

        var contextIds = candidates
            .SelectMany(instance => GetContextProperties(instance.WorkflowDefinition)
                .SelectMany(propertyName => GetReferenceIds(instance.GetProperty(propertyName))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (contextIds.Length == 0)
            return [];

        var contexts = await workflowInstanceRepository.GetByIds(contextIds, ct);
        return contexts
            .Where(course =>
            {
                var externalId = course.GetProperty("ExternalId")?.ToString();
                return !string.IsNullOrWhiteSpace(externalId) &&
                       launchCourseIdentifiers.Contains(externalId, StringComparer.OrdinalIgnoreCase);
            })
            .Select(course => course.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetContextProperties(string workflowDefinition)
    {
        if (!modelService.WorkflowDefinitions.TryGetValue(workflowDefinition, out var definition))
            return [];

        return definition.Properties.OfType<WorkflowPropertyDefinition>().Where(property =>
                string.Equals(property.WorkflowDefinition?.Name, "Context", StringComparison.OrdinalIgnoreCase))
            .Select(property => property.Name);
    }

    /// <summary>
    /// Returns the reference IDs stored in a BSON property value.
    /// Handles both a single ID and an array of IDs, ignoring null or blank values.
    /// </summary>
    private static IEnumerable<string> GetReferenceIds(BsonValue? value)
        => value switch
        {
            null or BsonNull => [],
            BsonArray values => values.SelectMany(GetReferenceIds),
            _ => value.ToString() is { } id && !string.IsNullOrWhiteSpace(id)
                ? [id]
                : []
        };
}