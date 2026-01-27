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
            versions.Add(new StepVersion
            {
                VersionNumber = versionNumber++,
                EventId = logEntry.EventId,
                SubmittedAt = logEntry.Timestamp,
                FormData = await ExtractFormDataAtTimestamp(instance, logEntry.EventId, logEntry.Timestamp, ct)
            });
        }

        return versions.OrderByDescending(v => v.SubmittedAt).ToList();
    }

    private async Task<Dictionary<string, object?>> ExtractFormDataAtTimestamp(
        WorkflowInstance instance,
        string formId,
        DateTime timestamp,
        CancellationToken ct)
    {
        try
        {
            var form = modelService.GetForm(instance, formId);
            if (form == null)
                return new Dictionary<string, object?>();

            var version = await instanceService.GetVersionAtTimestamp(instance.Id, timestamp, ct);
            var instanceAtVersion = await instanceService.GetAsOfVersion(instance.Id, version, ct);

            var result = new Dictionary<string, object?>();
            foreach (var field in form.PropertyDefinitions)
            {
                if (instanceAtVersion.Properties.TryGetValue(field.Name, out var value))
                {
                    result[field.Name] = ObjectContext.GetValue(value, field);
                }
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }
}