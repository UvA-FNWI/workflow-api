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
        var (allChildEvents, completionEvents) = DetermineEventSets(step);

        // Get all event log entries for ALL child events, ordered by timestamp
        var eventLogs = await eventRepository.GetEventLogEntriesForInstance(
            instance.Id, allChildEvents, ct);

        // Get submission events (create/update only), ordered chronologically
        var submissionEvents = eventLogs
            .Where(log => log.Operation is EventLogOperation.Create or EventLogOperation.Update)
            .OrderBy(log => log.Timestamp)
            .ToList();

        var versions = BuildVersions(step, submissionEvents, completionEvents);
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

    private static (List<string> AllEvents, List<string> CompletionEvents) DetermineEventSets(Step step)
    {
        if (step.Ends != null)
        {
            var ownEvents = step.Ends.GetAllEventIds().ToList();
            return (ownEvents, ownEvents);
        }

        if (!step.Children.Any())
            return (new List<string>(), new List<string>());

        var allChildEvents = step.Children
            .SelectMany(c => c.Ends?.GetAllEventIds() ?? Enumerable.Empty<string>())
            .Distinct()
            .ToList();

        if (step.HierarchyMode == StepHierarchyMode.Sequential)
        {
            var lastChild = step.Children.Last();
            var completionEvents = lastChild.Ends?.GetAllEventIds().ToList() ?? new List<string>();
            return (allChildEvents, completionEvents);
        }

        return (allChildEvents, allChildEvents);
    }

    private List<StepVersion> BuildVersions(
        Step step,
        List<InstanceEventLogEntry> submissionEvents,
        List<string> completionEventIds)
    {
        if (step.Ends == null && step.Children.Any())
        {
            return step.HierarchyMode == StepHierarchyMode.Sequential
                ? BuildSequentialVersions(submissionEvents, completionEventIds)
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
        List<string> completionEventIds)
    {
        var tempVersions = new List<(int VersionNumber, string EventId, DateTime Timestamp)>();
        int currentVersionNumber = 1;
        var completionEventSet = completionEventIds.ToHashSet();

        foreach (var logEntry in submissionEvents)
        {
            // All events in this cycle get the same version number
            tempVersions.Add((currentVersionNumber, logEntry.EventId, logEntry.Timestamp));

            // If this event marks the completion of the cycle (last child's event), increment version
            if (completionEventSet.Contains(logEntry.EventId))
            {
                currentVersionNumber++;
            }
        }

        // Group events by version number and consolidate
        var versionDrafts = tempVersions
            .GroupBy(v => v.VersionNumber)
            .Select(g => (
                VersionNumber: g.Key,
                EventIds: g.Select(v => v.EventId).ToList(),
                SubmittedAt: g.Max(v => v.Timestamp)))
            .Where(v => v.EventIds.Any(e => completionEventSet.Contains(e))) // Only complete versions
            .ToList();

        return BuildStepVersions(versionDrafts);
    }

    private static List<StepVersion> BuildParallelVersions(
        Step step,
        List<InstanceEventLogEntry> submissionEvents)
    {
        var tempVersions = new List<(int VersionNumber, string EventId, DateTime Timestamp)>();
        int currentVersionNumber = 1;

        // Map each child's end events to the child
        var childEventMap = new Dictionary<string, Step>();
        foreach (var child in step.Children)
        {
            var childEvents = child.Ends?.GetAllEventIds() ?? [];
            foreach (var eventId in childEvents)
            {
                childEventMap[eventId] = child;
            }
        }

        // Track which children have completed in the current cycle
        var completedChildrenInCycle = new HashSet<string>();
        var totalChildren = step.Children.Length;

        foreach (var logEntry in submissionEvents)
        {
            // All events in this cycle get the same version number
            tempVersions.Add((currentVersionNumber, logEntry.EventId, logEntry.Timestamp));

            // Determine which child this event belongs to
            if (childEventMap.TryGetValue(logEntry.EventId, out var child))
            {
                completedChildrenInCycle.Add(child.Name);

                // Check if all children have now completed
                if (completedChildrenInCycle.Count == totalChildren)
                {
                    // All children completed - this marks a version boundary
                    // Next event will be in a new version
                    currentVersionNumber++;

                    // Reset for next cycle
                    completedChildrenInCycle.Clear();
                }
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
                // Check if all children have at least one event in this version
                var childrenWithEvents = v.EventIds
                    .Select(e => childEventMap.GetValueOrDefault(e)?.Name)
                    .Where(name => name != null)
                    .Distinct()
                    .ToHashSet();
                return childrenWithEvents.Count == totalChildren;
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
}