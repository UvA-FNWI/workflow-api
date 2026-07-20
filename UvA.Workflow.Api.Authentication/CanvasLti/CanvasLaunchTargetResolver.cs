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

        var matchingCourseIds = await FindMatchingCourseIds(candidates, launchInfo.CourseIdentifiers, ct);
        var selected = candidates
                           .Where(i => matchingCourseIds.Contains(i.GetProperty("Course")?.ToString() ?? ""))
                           .OrderByDescending(i => i.CreatedOn)
                           .FirstOrDefault()
                       ?? candidates.OrderByDescending(i => i.CreatedOn).First();

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
        var instances = await GetCourseInstances(launchInfo.CourseIdentifiers, ct);
        var definitions = instances
            .Select(i => i.WorkflowDefinition)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => modelService.WorkflowDefinitions.GetValueOrDefault(name))
            .Where(definition => definition?.Screens is { Count: > 0 })
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

    private async Task<List<WorkflowInstance>> GetCourseInstances(
        IReadOnlyCollection<string> launchCourseIdentifiers,
        CancellationToken ct)
    {
        if (launchCourseIdentifiers.Count == 0)
            return [];

        var courseFilter = Builders<WorkflowInstance>.Filter.In(
            "Properties.ExternalId",
            launchCourseIdentifiers);
        var courses = await workflowInstanceRepository.GetByWorkflowDefinition("Context", courseFilter, ct);
        var courseIds = courses
            .Select(course => course.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (courseIds.Length == 0)
            return [];

        var instanceFilter = Builders<WorkflowInstance>.Filter.In("Properties.Course", courseIds);
        return (await workflowInstanceRepository.GetByFilter(instanceFilter, ct)).ToList();
    }

    private async Task<List<WorkflowInstance>> GetStudentInstances(string userId, CancellationToken ct)
    {
        if (!ObjectId.TryParse(userId, out var objectId))
            return [];

        var filter = Builders<WorkflowInstance>.Filter.Eq("Properties.Student._id", objectId);
        return (await workflowInstanceRepository.GetByFilter(filter, ct)).ToList();
    }

    private async Task<HashSet<string>> FindMatchingCourseIds(
        IEnumerable<WorkflowInstance> candidates,
        IReadOnlyCollection<string> launchCourseIdentifiers,
        CancellationToken ct)
    {
        if (launchCourseIdentifiers.Count == 0)
            return [];

        var courseIds = candidates
            .Select(i => i.GetProperty("Course")?.ToString())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;

        if (courseIds.Length == 0)
            return [];

        var courseInstances = await workflowInstanceRepository.GetByIds(courseIds!, ct);
        return courseInstances
            .Where(course =>
            {
                var externalId = course.GetProperty("ExternalId")?.ToString();
                return !string.IsNullOrWhiteSpace(externalId) &&
                       launchCourseIdentifiers.Contains(externalId, StringComparer.OrdinalIgnoreCase);
            })
            .Select(course => course.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}