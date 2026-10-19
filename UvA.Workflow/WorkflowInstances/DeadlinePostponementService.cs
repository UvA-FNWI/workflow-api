using UvA.Workflow.Infrastructure;
using UvA.Workflow.Journaling;
using UvA.Workflow.Jobs;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;
using DomainAction = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.WorkflowInstances;

public record PostponableDeadline(string Property, BilingualString Title, DateOnly Date);

public record DeadlinePostponement(string Property, DateOnly CurrentDate, DateOnly Date);

public class DeadlinePostponementService(
    ModelService modelService,
    RightsService rightsService,
    IWorkflowInstanceRepository repository,
    IInstanceJournalService journalService,
    InstanceService instanceService,
    JobService jobService)
{
    private record DeadlineCandidate(PostponableDeadline Deadline, Step[] Steps);

    public async Task<PostponableDeadline[]> GetDeadlines(WorkflowInstance instance)
    {
        var deadlines = new List<PostponableDeadline>();
        foreach (var candidate in GetDeadlineCandidates(instance).Values)
            if ((await GetAllowedActions(instance, candidate)).Length > 0)
                deadlines.Add(candidate.Deadline);

        return deadlines.ToArray();
    }

    private Dictionary<string, DeadlineCandidate> GetDeadlineCandidates(WorkflowInstance instance)
    {
        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var context = modelService.CreateContext(instance);
        var stepsByProperty = definition.AllSteps
            .Where(step => step.Deadline != null && step.Condition.IsMet(context) && !step.HasEnded(context))
            .ToLookup(step => step.Deadline!.Date.Trim());
        var candidates = new Dictionary<string, DeadlineCandidate>();

        // Exact property-name matching excludes expressions, literal dates and external references.
        foreach (var property in definition.Properties
                     .Where(property => property.DataType == DataType.Date && !property.IsArray &&
                                        stepsByProperty.Contains(property.Name)))
        {
            if (instance.GetProperty(property.Name) is not BsonDateTime date)
                continue;

            var deadline = new PostponableDeadline(property.Name, property.DisplayName,
                DateOnly.FromDateTime(date.ToLocalTime()));
            candidates.Add(property.Name, new DeadlineCandidate(deadline, stepsByProperty[property.Name].ToArray()));
        }

        return candidates;
    }

    private async Task<DomainAction[]> GetAllowedActions(WorkflowInstance instance, DeadlineCandidate candidate)
    {
        var actions = new List<DomainAction>();
        foreach (var step in candidate.Steps)
        {
            var allowed =
                await rightsService.GetAllowedActionsForStep(instance, step.Name, RoleAction.PostponeDeadlines);
            actions.AddRange(allowed.Where(action =>
                action.PropertyDefinition == null || action.PropertyDefinition == candidate.Deadline.Property));
        }

        return actions.Distinct().ToArray();
    }

    public async Task<EffectResult> Postpone(WorkflowInstance instance, DeadlinePostponement[] changes, string reason,
        User realUser, CancellationToken ct)
    {
        var trimmedReason = reason?.Trim();
        if (string.IsNullOrEmpty(trimmedReason) || trimmedReason.Length > 1000)
            throw Invalid("InvalidPostponementReason", "Reason must contain between 1 and 1,000 characters.");
        if (changes == null || changes.Length == 0 || changes.Any(change => change == null) ||
            changes.Select(change => change.Property).Distinct().Count() != changes.Length)
            throw Invalid("InvalidDeadlineChanges", "Select each deadline at most once, and at least one deadline.");

        var candidates = GetDeadlineCandidates(instance);
        var proposed = new WorkflowInstance
        {
            Id = instance.Id,
            WorkflowDefinition = instance.WorkflowDefinition,
            CurrentStep = instance.CurrentStep,
            CreatedOn = instance.CreatedOn,
            ParentId = instance.ParentId,
            Events = instance.Events,
            Properties = new Dictionary<string, BsonValue>(instance.Properties)
        };
        var entries = new List<PropertyChangeEntry>();
        var actions = new HashSet<DomainAction>();
        foreach (var change in changes)
        {
            if (string.IsNullOrWhiteSpace(change.Property) ||
                !candidates.TryGetValue(change.Property, out var candidate))
                throw Invalid("InvalidDeadlineProperty", "The selected property is not an available deadline.");
            var allowed = await GetAllowedActions(instance, candidate);
            if (allowed.Length == 0)
                throw new ForbiddenWorkflowActionException(instance.Id, RoleAction.PostponeDeadlines, change.Property);
            actions.UnionWith(allowed);

            var deadline = candidate.Deadline;
            if (change.CurrentDate != deadline.Date)
                throw Invalid("DeadlineChanged", "The deadline has changed. Refresh and try again.");
            if (change.Date <= deadline.Date)
                throw Invalid("InvalidDeadlineDate", "The new deadline must be a later date.");

            // Date properties are calendar dates in the server's configured timezone.
            BsonValue newValue = new BsonDateTime(change.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local));
            proposed.SetProperty(newValue, change.Property);
            entries.Add(PropertyChangeEntry.Create(
                change.Property, instance.GetProperty(change.Property), realUser, reason: trimmedReason));
        }

        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var context = modelService.CreateContext(proposed);
        foreach (var change in changes)
        {
            var property = definition.Properties.Get(change.Property);
            if (!property.Validation.IsMet(context))
                throw Invalid("InvalidDeadlineDate", property.Validation!.Message?.En ?? "Invalid value");
        }

        var unchanged = Builders<WorkflowInstance>.Filter.And(entries.Select(entry =>
            Builders<WorkflowInstance>.Filter.Eq(item => item.Properties[entry.Path], entry.OldValue)));
        var saved = await repository.TryUpdateFields(instance.Id, unchanged, Builders<WorkflowInstance>.Update.Combine(
            changes.Select(change => Builders<WorkflowInstance>.Update.Set(
                item => item.Properties[change.Property], proposed.GetProperty(change.Property)))), ct);
        if (!saved)
            throw Invalid("DeadlineChanged", "The deadline has changed. Refresh and try again.");
        foreach (var entry in entries)
        {
            instance.SetProperty(proposed.GetProperty(entry.Path), entry.Path);
            await journalService.LogPropertyChange(instance.Id, entry, ct);
        }

        var result = new EffectResult();
        var actionsWithEffects = actions.Where(action => action.OnAction.Length > 0).ToArray();
        foreach (var action in actionsWithEffects)
            result += await jobService.CreateAndRunJob(instance, action, realUser, null, ct);
        if (actionsWithEffects.Length > 0)
            await instanceService.UpdateCurrentStep(instance, ct);
        return result;

        InvalidWorkflowStateException Invalid(string code, string message) => new(instance.Id, code, message);
    }
}