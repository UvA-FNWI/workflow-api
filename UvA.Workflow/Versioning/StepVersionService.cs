using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Journaling;

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
    IInstanceEventRepository eventRepository,
    IInstanceJournalService journalService) : IStepVersionService
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

        // Get instance journal to track property changes
        var journal = await journalService.GetInstanceJournal(instance.Id, false, ct);

        // Build version chain
        var versions = await BuildVersionChain(instance, stepEvents, eventLogs, journal);

        return new StepVersions
        {
            StepName = stepName,
            Versions = versions
        };
    }

    private async Task<List<StepVersion>> BuildVersionChain(
        WorkflowInstance instance,
        List<string> eventIds,
        List<InstanceEventLogEntry> eventLogs,
        InstanceJournalEntry? journal)
    {
        var versions = new List<StepVersion>();
        int stepVersionNumber = 1;

        // Group logs by event ID and get creation/update events only, ordered by date
        var submissionEvents = eventLogs
            .Where(log => eventIds.Contains(log.EventId) &&
                          log.Operation is EventLogOperation.Create or EventLogOperation.Update)
            .GroupBy(log => new { log.EventId, log.EventDate })
            .OrderBy(g => g.Key.EventDate)
            .ToList();

        foreach (var eventGroup in submissionEvents)
        {
            var latestLog = eventGroup.OrderByDescending(e => e.Timestamp).First();

            // Use the EventDate from the LOG, not from the current instance state
            // The instance only has the latest event date, but the log has the historical dates
            var submissionTime = latestLog.EventDate ?? latestLog.Timestamp;

            // Find the highest journal version that existed just BEFORE this submission
            // Changes are logged with the current version, then IncrementVersion is called
            // So we want the highest version of changes that were made BEFORE this submission
            int journalVersionAtSubmission = 0; // Default to version 0

            if (journal is { PropertyChanges.Length: > 0 })
            {
                // Find all changes that happened before this submission
                var changesBeforeSubmission = journal.PropertyChanges
                    .Where(pc => pc.Timestamp < submissionTime)
                    .ToList();

                if (changesBeforeSubmission.Any())
                {
                    // The journal version at submission time is the max version of changes made before it
                    journalVersionAtSubmission = changesBeforeSubmission.Max(pc => pc.Version);
                }
            }

            versions.Add(new StepVersion
            {
                VersionNumber = stepVersionNumber++,
                EventId = latestLog.EventId,
                SubmittedAt = submissionTime,
                FormData = await ExtractFormDataAtVersion(instance, latestLog.EventId, journalVersionAtSubmission,
                    journal)
            });
        }

        return versions.OrderByDescending(v => v.SubmittedAt).ToList();
    }

    private Task<Dictionary<string, object?>> ExtractFormDataAtVersion(
        WorkflowInstance instance,
        string formId,
        int journalVersion,
        InstanceJournalEntry? journal)
    {
        try
        {
            var form = modelService.GetForm(instance, formId);
            if (form == null)
                return Task.FromResult(new Dictionary<string, object?>());

            // Create a temporary instance with cloned properties to revert changes on
            var instanceAtVersion = new WorkflowInstance
            {
                Properties = instance.Properties.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value?.DeepClone() ?? BsonNull.Value
                )
            };

            // Revert all changes after the target version (same logic as WorkflowInstanceService.GetAsOfVersion)
            if (journal is { PropertyChanges.Length: > 0 })
            {
                foreach (var change in journal.PropertyChanges
                             .Where(pc => pc.Version > journalVersion)
                             .OrderByDescending(pc => pc.Timestamp))
                {
                    instanceAtVersion.SetProperty(change.OldValue, change.Path.Split('.'));
                }
            }

            // Extract all form properties
            var result = new Dictionary<string, object?>();
            foreach (var field in form.PropertyDefinitions)
            {
                if (instanceAtVersion.Properties.TryGetValue(field.Name, out var value))
                {
                    result[field.Name] = ObjectContext.GetValue(value, field);
                }
            }

            return Task.FromResult(result);
        }
        catch
        {
            return Task.FromResult(new Dictionary<string, object?>());
        }
    }
}