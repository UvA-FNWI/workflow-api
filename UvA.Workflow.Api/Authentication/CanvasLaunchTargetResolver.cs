namespace UvA.Workflow.Api.Authentication;

public class CanvasLaunchTargetResolver(
    IWorkflowInstanceRepository workflowInstanceRepository,
    IOptions<CanvasLtiOptions> options,
    ILogger<CanvasLaunchTargetResolver> logger)
{
    private readonly CanvasLtiOptions options = options.Value;

    public async Task<string> ResolveTarget(User user, CanvasLaunchInfo launchInfo, CancellationToken ct)
    {
        if (launchInfo.IsTeacher)
            return options.TeacherTarget;

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