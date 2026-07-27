using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Versioning;

public record StepVersion
{
    public int VersionNumber { get; init; }
    public List<string> EventIds { get; init; } = [];
    public DateTime SubmittedAt { get; init; }
}

public interface IStepVersionService
{
    Task<List<StepVersion>> GetStepVersions(WorkflowInstance instance, string stepName, CancellationToken ct);

    List<StepVersion> GetStepVersions(
        WorkflowInstance instance,
        string stepName,
        IEnumerable<InstanceEventLogEntry> eventLogs);
}

public class StepVersionService(
    ModelService modelService,
    IInstanceEventRepository eventRepository) : IStepVersionService
{
    public async Task<List<StepVersion>> GetStepVersions(
        WorkflowInstance instance,
        string stepName,
        CancellationToken ct)
    {
        var workflowDef = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var step = ResolveTargetStep(workflowDef, stepName);
        var allChildEvents = DetermineEventSets(step).AllEvents;

        // Get all event log entries for ALL child events, ordered by timestamp
        var eventLogs = await eventRepository.GetEventLogEntriesForInstance(
            instance.Id, allChildEvents, ct);

        return GetStepVersions(instance, stepName, eventLogs);
    }

    public List<StepVersion> GetStepVersions(
        WorkflowInstance instance,
        string stepName,
        IEnumerable<InstanceEventLogEntry> eventLogs)
    {
        var workflowDef = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var step = ResolveTargetStep(workflowDef, stepName);
        var (allChildEvents, completionCondition) = DetermineEventSets(step);
        var allChildEventSet = allChildEvents.ToHashSet();

        // Get submission events (create/update only), ordered chronologically
        var submissionEvents = eventLogs
            .Where(log => allChildEventSet.Contains(log.EventId))
            .Where(log => log.Operation is EventLogOperation.Create or EventLogOperation.Update)
            .OrderBy(log => log.Timestamp)
            .ToList();

        var versions = BuildVersions(step, submissionEvents, completionCondition);

        // The latest completed cycle is the live state of a completed step, not history.
        if (versions.Count > 0 && step.HasEnded(modelService.CreateContext(instance)))
            versions.RemoveAt(versions.Count - 1);

        var orderedVersions = versions.OrderByDescending(v => v.SubmittedAt).ToList();

        return orderedVersions;
    }

    private static Step ResolveTargetStep(WorkflowDefinition workflowDef, string stepName)
    {
        var step = workflowDef.AllSteps.FirstOrDefault(s => s.Name == stepName);
        if (step == null)
            throw new EntityNotFoundException("Step", stepName);

        var parentStep = workflowDef.AllSteps.FirstOrDefault(s =>
            s.Children.Any(c => c.Name == stepName));

        return parentStep is { Children.Length: > 1 }
            ? parentStep
            : step;
    }

    private static (List<string> AllEvents, Condition? CompletionCondition) DetermineEventSets(Step step)
    {
        if (step.Ends != null)
        {
            var ownEvents = step.Ends.GetAllEventIds().ToList();
            return (ownEvents, step.Ends);
        }

        if (!step.Children.Any())
            return ([], null);

        var allChildEvents = step.Children
            .SelectMany(GetStepEventIds)
            .Distinct()
            .ToList();

        return (allChildEvents, GetCompletionCondition(step));
    }

    private List<StepVersion> BuildVersions(
        Step step,
        List<InstanceEventLogEntry> submissionEvents,
        Condition? completionCondition)
    {
        if (step.Ends == null && step.Children.Any())
            return BuildMultiEventVersions(submissionEvents, completionCondition);

        return BuildSingleEventVersions(submissionEvents);
    }

    private static List<StepVersion> BuildSingleEventVersions(
        List<InstanceEventLogEntry> submissionEvents)
    {
        var versionDrafts = submissionEvents
            .Select((logEntry, index) => (
                VersionNumber: index + 1,
                EventIds: new List<string> { logEntry.EventId },
                SubmittedAt: logEntry.Timestamp))
            .ToList();

        return BuildStepVersions(versionDrafts);
    }

    private static List<StepVersion> BuildMultiEventVersions(
        List<InstanceEventLogEntry> submissionEvents,
        Condition? completionCondition)
    {
        var versionDrafts =
            new List<(int VersionNumber, List<string> EventIds, DateTime SubmittedAt)>();
        var currentVersionEvents = new List<InstanceEventLogEntry>();
        var currentVersionEventIds = new HashSet<string>();

        foreach (var logEntry in submissionEvents)
        {
            currentVersionEvents.Add(logEntry);
            currentVersionEventIds.Add(logEntry.EventId);

            if (!completionCondition.IsMet(currentVersionEventIds))
                continue;

            versionDrafts.Add((
                VersionNumber: versionDrafts.Count + 1,
                EventIds: currentVersionEvents.Select(log => log.EventId).ToList(),
                SubmittedAt: currentVersionEvents.Max(log => log.Timestamp)
            ));
            currentVersionEvents.Clear();
            currentVersionEventIds.Clear();
        }

        return BuildStepVersions(versionDrafts);
    }

    private static List<StepVersion> BuildStepVersions(
        List<(int VersionNumber, List<string> EventIds, DateTime SubmittedAt)> versionDrafts)
    {
        return versionDrafts
            .Select(versionDraft => new StepVersion
            {
                VersionNumber = versionDraft.VersionNumber,
                EventIds = versionDraft.EventIds,
                SubmittedAt = versionDraft.SubmittedAt
            })
            .ToList();
    }

    private static IEnumerable<string> GetStepEventIds(Step step)
    {
        if (step.Ends != null)
            return step.Ends.GetAllEventIds();

        return step.Children.SelectMany(GetStepEventIds);
    }

    private static Condition? GetCompletionCondition(Step step)
    {
        if (step.Ends != null)
            return step.Ends;

        if (!step.Children.Any())
            return null;

        return step.HierarchyMode == StepHierarchyMode.Sequential
            ? GetCompletionCondition(step.Children.Last())
            : new Condition
            {
                Logical = new Logical
                {
                    Operator = LogicalOperator.And,
                    Children = step.Children
                        .Select(GetCompletionCondition)
                        .Where(condition => condition != null)
                        .Cast<Condition>()
                        .ToArray()
                }
            };
    }
}