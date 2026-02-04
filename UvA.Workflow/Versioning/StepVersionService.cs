using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;

namespace UvA.Workflow.Versioning;

public record StepVersion
{
    public int VersionNumber { get; init; }
    public string EventId { get; init; } = null!;
    public DateTime SubmittedAt { get; init; }
    public int InstanceVersion { get; init; }
}

public interface IStepVersionService
{
    Task<List<StepVersion>> GetStepVersions(WorkflowInstance instance, string stepName, CancellationToken ct);
}

public class StepVersionService(
    ModelService modelService,
    IInstanceEventRepository eventRepository,
    WorkflowInstanceService instanceService) : IStepVersionService
{
    public async Task<List<StepVersion>> GetStepVersions(
        WorkflowInstance instance,
        string stepName,
        CancellationToken ct)
    {
        var workflowDef = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var step = workflowDef.AllSteps.FirstOrDefault(s => s.Name == stepName);

        if (step == null)
            throw new EntityNotFoundException("Step", stepName);

        // Get all events for this step and child steps
        var stepEvents = new List<string>();
        if (step.EndEvent != null)
            stepEvents.Add(step.EndEvent);
        stepEvents.AddRange(step.Children.Select(c => c.EndEvent).Where(e => e != null)!);

        // Get all event log entries for these events, ordered by timestamp
        var eventLogs = await eventRepository.GetEventLogEntriesForInstance(
            instance.Id, stepEvents, ct);

        // Get submission events (create/update only), ordered chronologically
        var submissionEvents = eventLogs
            .Where(log => stepEvents.Contains(log.EventId) &&
                          log.Operation is EventLogOperation.Create or EventLogOperation.Update)
            .OrderBy(log => log.Timestamp)
            .ToList();

        var versions = new List<StepVersion>();
        int versionNumber = 1;

        foreach (var logEntry in submissionEvents)
        {
            var instanceVersion = await instanceService.GetVersionAtTimestamp(instance.Id, logEntry.Timestamp, ct);

            versions.Add(new StepVersion
            {
                VersionNumber = versionNumber++,
                EventId = logEntry.EventId,
                SubmittedAt = logEntry.Timestamp,
                InstanceVersion = instanceVersion
            });
        }


        var orderedVersions = versions.OrderByDescending(v => v.SubmittedAt).ToList();

        // Determine which version is currently being shown (active)
        var activeEvents = instance.Events.WhereActive(instance, workflowDef)
            .Select(e => e.Key)
            .Where(stepEvents.Contains)
            .ToHashSet();

        var currentlyShownVersion = orderedVersions
            .FirstOrDefault(v => activeEvents.Contains(v.EventId));

        // If there's only one version and it's currently being shown, return empty list
        if (orderedVersions.Count <= 1 && currentlyShownVersion != null)
            return new List<StepVersion>();

        // If a version is currently being shown, exclude it from the list
        if (currentlyShownVersion != null)
            return orderedVersions.Where(v => v.VersionNumber != currentlyShownVersion.VersionNumber).ToList();

        return orderedVersions;
    }
}