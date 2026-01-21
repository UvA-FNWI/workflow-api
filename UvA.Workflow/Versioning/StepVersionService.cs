using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;

namespace UvA.Workflow.Versioning;

public record StepVersion
{
    public int VersionNumber { get; init; }
    public string EventId { get; init; } = null!;
    public DateTime SubmittedAt { get; init; }
    public Dictionary<string, object?> FormData { get; init; } = new();
}

public record StepVersions
{
    public string StepName { get; init; } = null!;
    public List<StepVersion> Versions { get; init; } = new();
}

public interface IStepVersionService
{
    Task<StepVersions> GetStepVersions(WorkflowInstance instance, string stepName, CancellationToken ct);
}

public class StepVersionService(
    ModelService modelService,
    IInstanceEventRepository eventRepository) : IStepVersionService
{
    public async Task<StepVersions> GetStepVersions(
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

        // Build version chain by analyzing suppression relationships
        var versions = BuildVersionChain(instance, stepEvents, eventLogs);

        return new StepVersions
        {
            StepName = stepName,
            Versions = versions
        };
    }

    private List<StepVersion> BuildVersionChain(
        WorkflowInstance instance,
        List<string> eventIds,
        List<InstanceEventLogEntry> eventLogs)
    {
        var versions = new List<StepVersion>();
        int versionNumber = 1;

        // Group logs by event ID and get creation/update events only
        var submissionEvents = eventLogs
            .Where(log => eventIds.Contains(log.EventId) &&
                          log.Operation is EventLogOperation.Create or EventLogOperation.Update)
            .GroupBy(log => new { log.EventId, log.EventDate })
            .OrderBy(g => g.Key.EventDate);

        foreach (var eventGroup in submissionEvents)
        {
            var latestLog = eventGroup.OrderByDescending(e => e.Timestamp).First();
            var currentEvent = instance.Events.GetValueOrDefault(latestLog.EventId);

            if (currentEvent?.Date != null)
            {
                versions.Add(new StepVersion
                {
                    VersionNumber = versionNumber++,
                    EventId = latestLog.EventId,
                    SubmittedAt = currentEvent.Date.Value,
                    FormData = ExtractFormData(instance, latestLog.EventId)
                });
            }
        }

        return versions.OrderByDescending(v => v.SubmittedAt).ToList();
    }

    private Dictionary<string, object?> ExtractFormData(
        WorkflowInstance instance,
        string formId)
    {
        try
        {
            var form = modelService.GetForm(instance, formId);
            var result = new Dictionary<string, object?>();

            foreach (var field in form.PropertyDefinitions)
            {
                if (instance.Properties.TryGetValue(field.Name, out var value))
                {
                    result[field.Name] = ObjectContext.GetValue(value, field);
                }
            }

            return result;
        }
        catch
        {
            // If form doesn't exist or has errors, return empty dictionary
            return new Dictionary<string, object?>();
        }
    }
}