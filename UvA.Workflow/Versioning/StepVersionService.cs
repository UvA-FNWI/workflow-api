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
            return (new List<string>(), null);

        var allChildEvents = step.Children
            .SelectMany(GetStepEventIds)
            .Distinct()
            .ToList();

        if (step.HierarchyMode == StepHierarchyMode.Sequential)
        {
            var lastChild = step.Children.Last();
            return (allChildEvents, GetCompletionCondition(lastChild));
        }

        return (allChildEvents, GetCompletionCondition(step));
    }

    private List<StepVersion> BuildVersions(
        Step step,
        List<InstanceEventLogEntry> submissionEvents,
        Condition? completionCondition)
    {
        if (step.Ends == null && step.Children.Any())
        {
            return step.HierarchyMode == StepHierarchyMode.Sequential
                ? BuildSequentialVersions(submissionEvents, completionCondition)
                : BuildParallelVersions(step, submissionEvents);
        }

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

    private static List<StepVersion> BuildSequentialVersions(
        List<InstanceEventLogEntry> submissionEvents,
        Condition? completionCondition)
    {
        var tempVersions = new List<(int VersionNumber, string EventId, DateTime Timestamp)>();
        int currentVersionNumber = 1;
        var currentCycleEventIds = new HashSet<string>();

        foreach (var logEntry in submissionEvents)
        {
            // All events in this cycle get the same version number
            tempVersions.Add((currentVersionNumber, logEntry.EventId, logEntry.Timestamp));
            currentCycleEventIds.Add(logEntry.EventId);

            // If the last child's completion condition is met, the next event starts a new version.
            if (completionCondition.IsMet(currentCycleEventIds))
            {
                currentVersionNumber++;
                currentCycleEventIds.Clear();
            }
        }

        // Group events by version number and consolidate
        var versionDrafts = tempVersions
            .GroupBy(v => v.VersionNumber)
            .Select(g => (
                VersionNumber: g.Key,
                EventIds: g.Select(v => v.EventId).ToList(),
                SubmittedAt: g.Max(v => v.Timestamp)))
            .Where(v => completionCondition.IsMet(v.EventIds)) // Only complete versions
            .ToList();

        return BuildStepVersions(versionDrafts);
    }

    private static List<StepVersion> BuildParallelVersions(
        Step step,
        List<InstanceEventLogEntry> submissionEvents)
    {
        var tempVersions = new List<(int VersionNumber, string EventId, DateTime Timestamp)>();
        int currentVersionNumber = 1;

        var childCompletionConditions = step.Children
            .ToDictionary(child => child.Name, GetCompletionCondition);

        // Track which children have completed in the current cycle
        var completedChildrenInCycle = new HashSet<string>();
        var currentCycleEventIds = new HashSet<string>();
        var totalChildren = step.Children.Length;

        foreach (var logEntry in submissionEvents)
        {
            // All events in this cycle get the same version number
            tempVersions.Add((currentVersionNumber, logEntry.EventId, logEntry.Timestamp));
            currentCycleEventIds.Add(logEntry.EventId);

            foreach (var (childName, completionCondition) in childCompletionConditions)
            {
                if (!completedChildrenInCycle.Contains(childName) &&
                    completionCondition.IsMet(currentCycleEventIds))
                    completedChildrenInCycle.Add(childName);
            }

            // Check if all children have now completed
            if (completedChildrenInCycle.Count == totalChildren)
            {
                // All children completed - this marks a version boundary
                // Next event will be in a new version
                currentVersionNumber++;

                // Reset for next cycle
                completedChildrenInCycle.Clear();
                currentCycleEventIds.Clear();
            }
        }

        // Group events by version number and consolidate
        var versionDrafts = tempVersions
            .GroupBy(v => v.VersionNumber)
            .Select(g => (
                VersionNumber: g.Key,
                EventIds: g.Select(v => v.EventId).ToList(),
                SubmittedAt: g.Max(v => v.Timestamp)))
            .Where(v =>
            {
                // Check if all child completion conditions are met in this version
                return childCompletionConditions.Values.All(condition =>
                    condition.IsMet(v.EventIds));
            })
            .ToList();

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