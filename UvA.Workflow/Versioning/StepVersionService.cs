using UvA.Workflow.Events;
using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Versioning;

public record StepVersionSubmission(string FormName, DateTime DateSubmitted, string[] EventIds, string? Step = null);

public record StepVersion
{
    public int VersionNumber { get; init; }
    public DateTime? CompletionTimestamp { get; set; }
    public DateTime? FrozenAt { get; set; }
    public List<StepVersionSubmission> Submissions { get; init; } = [];
    internal List<StepVersionSubmission> SubmissionCandidates { get; } = [];
}

public class StepVersionHistoryUnavailableException(string reason) : Exception(reason);

public record StepVersions(StepVersion? Current, List<StepVersion> History);

public interface IStepVersionService
{
    Dictionary<string, StepVersions> GetStepVersions(WorkflowInstance instance, WorkflowInstanceHistory history);
}

/// <summary>
/// Reconstructs step versions from history. A version is one attempt at a step, not one form submission.
/// </summary>
/// <remarks>
/// <para>
/// A step's first version starts when workflow progression reaches it. Completion updates that version;
/// it does not start another. A rejection/reset freezes the old version, and the replacement starts when
/// the step is reached again. Later submissions join that replacement. Until then, the frozen version remains Current.
/// </para>
/// <para>
/// Top-level steps have versions. Nested steps also have versions when they can restart independently.
/// Their submissions contribute to their own version and to enclosing steps' versions.
/// For example, Thesis contains Proposal and Writing, and Writing contains Draft and Review.
/// Requesting a Writing revision freezes Writing v1 and removes its submissions from Thesis v1.
/// Thesis stays at v1; Writing starts v2 when reached again. Restarting Thesis freezes both steps,
/// but Writing's replacement waits until progression reaches Writing again.
/// </para>
/// <para>
/// The implementation has three responsibilities: WorkflowVersionRules interprets the definition,
/// HistoryReconstruction walks recorded changes in order, and StepAttemptHistory maintains one step's
/// versions and submission membership. Viewer permissions are applied by callers after reconstruction.
/// </para>
/// </remarks>
/// <exception cref="StepVersionHistoryUnavailableException">
/// History cannot be reconstructed reliably, for example because merged answer edits obscure which version they belong to.
/// </exception>
public class StepVersionService(ModelService modelService) : IStepVersionService
{
    public Dictionary<string, StepVersions> GetStepVersions(WorkflowInstance instance, WorkflowInstanceHistory history)
    {
        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        ValidateProgressionHistory(definition, history);
        var rules = new WorkflowVersionRules(definition);
        return new HistoryReconstruction(modelService, definition, instance, history, rules).Reconstruct();
    }

    /// <summary>
    /// Uses the form's explicit step, or the common enclosing step of actions that submit it.
    /// Reconstruction and API display use this same mapping so a submission belongs to the same step in both.
    /// </summary>
    public static Step? ResolveFormStep(WorkflowDefinition definition, Form form)
        => form.Step != null
            ? definition.AllSteps.GetOrDefault(form.Step)
            : CommonAncestor(definition.AllSteps.Where(s => s.Actions.Any(a =>
                a.Type == RoleAction.Submit && a.MatchesForm(form.Name))).ToArray());

    /// <summary>
    /// Evaluates the workflow at past timestamps, applying one recorded event at a time.
    /// It controls ordering; StepAttemptHistory owns all changes to version contents.
    /// </summary>
    private sealed class HistoryReconstruction(
        ModelService modelService,
        WorkflowDefinition definition,
        WorkflowInstance instance,
        WorkflowInstanceHistory history,
        WorkflowVersionRules rules)
    {
        private readonly Dictionary<string, StepAttemptHistory> _histories = rules.VersionedSteps.ToDictionary(
            step => step.Name, step => new StepAttemptHistory(step, definition, history));

        private SortedSet<DateTime> _pendingDates = [];
        private DateTime _now;

        /// <summary>
        /// Rebuilds version history by evaluating the workflow at each time its state could change.
        /// Attempt boundaries are inferred from these historical states because they are not stored separately.
        /// </summary>
        public Dictionary<string, StepVersions> Reconstruct()
        {
            // Undo is resolved first. Removing a restart operation also removes its version boundary.
            var logs = EventHistory.Project(history.EventLogs).ToList();
            // Only properties come from snapshots: events must be applied individually, even at equal timestamps.
            var propertyHistory = history with { EventLogs = [] };
            // Audit timestamps determine replay order; EventDate still identifies the submission's effective date.
            var logsByDate = logs.GroupBy(log => log.Timestamp)
                .ToDictionary(group => group.Key, group => group.ToArray());
            _now = DateTime.UtcNow;
            // Seed the times to visit: recorded changes, initial reach at creation, and the state as of now.
            // This is a sorted set so a timestamp shared by several changes is visited only once.
            _pendingDates =
            [
                .. logsByDate.Keys
                    .Concat(history.Journal?.PropertyChanges.Select(change => change.Timestamp) ?? [])
                    .Append(instance.CreatedOn).Append(_now)
            ];
            var events = new Dictionary<string, InstanceEvent>();

            // Reached: start the first or replacement version, even without submissions.
            // Completed: update that version. Restarted: freeze it until the step is reached again.
            // Visit condition dates too, so time-driven progression does not wait for the next recorded event.
            while (_pendingDates.Count != 0)
            {
                var date = _pendingDates.Min;
                _pendingDates.Remove(date);
                // Restore properties and the evaluation clock to this time; carry events forward from the last visit.
                var snapshot = propertyHistory.GetInstanceAt(instance, date, modelService);
                snapshot.Events = events;

                // Account for progression caused by time/properties before applying this timestamp's events.
                ScheduleConditionDates(snapshot, date);
                StartVersionsForReachedSteps(snapshot);
                // Project already ordered equal-time logs by ID. Apply them individually to retain reset boundaries.
                foreach (var log in logsByDate.GetValueOrDefault(date) ?? [])
                    ApplyRecordedEvent(snapshot, log, date);

                // Property changes and deadlines can complete a step even when no event was recorded.
                UpdateCurrentVersions(snapshot, date);
                // The next property snapshot starts with exactly the event state reached here.
                events = snapshot.Events;
            }

            return _histories.ToDictionary(pair => pair.Key, pair => pair.Value.GetVersions());
        }

        /// <summary>
        /// Applies one event change, records its submissions, and closes any versions ended by a rejection/reset.
        /// Then reevaluates progression: the event may make a replacement version or another step reachable.
        /// </summary>
        private void ApplyRecordedEvent(WorkflowInstance snapshot, InstanceEventLogEntry log, DateTime date)
        {
            var before = new WorkflowInstance { Events = snapshot.Events };
            // Restart detection needs independent event states on either side of this operation.
            snapshot.Events = new Dictionary<string, InstanceEvent>(snapshot.Events);
            if (log.Operation == EventLogOperation.Delete)
                snapshot.Events.Remove(log.EventId);
            else
                snapshot.Events[log.EventId] = new InstanceEvent { Id = log.EventId, Date = log.EventDate };

            var restartedStep = rules.DetermineRestart(log, before, snapshot);
            // A rejection submission belongs to the version it ends.
            RecordSubmissions(log);
            if (restartedStep != null)
                FreezeVersionsInRestartScope(restartedStep, date);
            ScheduleConditionDates(snapshot, date);
            StartVersionsForReachedSteps(snapshot);
            UpdateCurrentVersions(snapshot, date);
        }

        /// <summary>
        /// Finds forms submitted by this event and records each in its step's version and enclosing versions.
        /// This lets a Thesis version include Draft/Review work while Writing also keeps its independent history.
        /// </summary>
        private void RecordSubmissions(InstanceEventLogEntry log)
        {
            if (log.Operation is not (EventLogOperation.Create or EventLogOperation.Update))
                return;
            foreach (var form in definition.Forms.Where(form =>
                         FormSubmissionState.GetSubmissionEventIds(form).Contains(log.EventId)))
            {
                var formStep = ResolveFormStep(definition, form);
                if (formStep == null)
                    continue;
                foreach (var stepHistory in
                         _histories.Values.Where(stepHistory => IsWithin(formStep, stepHistory.Step)))
                    stepHistory.RecordSubmission(form, formStep, log);
            }
        }

        /// <summary>
        /// Ends the current versions in the restarted subtree and removes their contributions from enclosing versions.
        /// Replacement versions are deferred until progression actually reaches each affected step again.
        /// </summary>
        private void FreezeVersionsInRestartScope(Step restartedStep, DateTime date)
        {
            // Restart the whole subtree, including children whose replacements cannot start yet.
            foreach (var stepHistory in
                     _histories.Values.Where(stepHistory => IsWithin(stepHistory.Step, restartedStep)))
                stepHistory.FreezeCurrentVersionOnRestart(date);

            // Enclosing steps keep their version number, but the restarted child's forms must contribute again.
            foreach (var stepHistory in _histories.Values.Where(stepHistory =>
                         stepHistory.Step != restartedStep && IsWithin(restartedStep, stepHistory.Step)))
                stepHistory.RemoveSubmissionsFrom(restartedStep);
        }

        /// <summary>
        /// Finds dates in each step's condition and end condition, and adds them to the reconstruction's pending dates.
        /// This lets the loop evaluate changes caused by time passing, even without a recorded event at that time.
        /// Called before and after events because an event can supply the date needed to calculate a condition's deadline.
        /// </summary>
        /// <remarks>
        /// For example, an end condition can require one day to pass after ProposalSubmitted.
        /// After a Monday 09:00 submission, the end time can be calculated as Tuesday 09:00. Add that time so the loop
        /// checks completion then, even if the next recorded event is on Wednesday. This queues a historical check, not a job.
        /// Only add dates after the snapshot time and no later than the captured current time; future completion is not inferred.
        /// </remarks>
        private void ScheduleConditionDates(WorkflowInstance snapshot, DateTime date)
        {
            var context = modelService.CreateContext(snapshot);
            foreach (var boundary in definition.AllSteps.SelectMany(step =>
                         ConditionDates(step.Condition, context).Concat(ConditionDates(step.Ends, context))))
                // Never revisit this time or insert a past boundary; the sorted set also deduplicates future ones.
                if (boundary > date && boundary <= _now)
                    _pendingDates.Add(boundary);
        }

        /// <summary>
        /// Finds dates and evaluated deadlines inside a condition, including its logical and named children.
        /// A nested date still matters even when the enclosing condition combines it with other requirements.
        /// </summary>
        private static IEnumerable<DateTime> ConditionDates(Condition? condition, ObjectContext context)
        {
            if (condition?.Date != null && context.Get(condition.Date.Source) is DateTime date)
                yield return date.ToUniversalTime();
            if (condition?.Deadline?.Evaluate(context) is { } deadline)
                yield return deadline.UtcDateTime;
            foreach (var child in condition?.Logical?.Children ?? [])
            foreach (var boundary in ConditionDates(child, context))
                yield return boundary;
            if (condition?.NamedCondition != null)
                foreach (var boundary in ConditionDates(condition.NamedCondition, context))
                    yield return boundary;
        }

        /// <summary>
        /// Uses workflow progression to decide which steps have been reached, then starts any missing/replacement versions.
        /// A true condition alone is insufficient: ordering and parent availability also matter. Reached children
        /// include their enclosing steps so grouping parents get versions even without their own submission.
        /// </summary>
        private void StartVersionsForReachedSteps(WorkflowInstance snapshot)
        {
            snapshot.CurrentStep = modelService.FindOpenStep(snapshot)?.Name;
            var context = modelService.CreateContext(snapshot);
            var reachedSteps = modelService.GetActiveSteps(snapshot)
                .Select(name => definition.AllSteps.Get(name))
                .Where(step => Ancestors(step).All(ancestor => ancestor.Condition.IsMet(context)))
                .SelectMany(Ancestors).ToHashSet();
            foreach (var stepHistory in _histories.Values.Where(stepHistory => reachedSteps.Contains(stepHistory.Step)))
                stepHistory.StartVersionWhenReached();
        }

        /// <summary>
        /// Updates effective submissions and completion for all open versions using the snapshot's historical clock.
        /// Leaves frozen versions untouched, including children waiting to be reached again after a parent reset.
        /// </summary>
        private void UpdateCurrentVersions(WorkflowInstance snapshot, DateTime date)
        {
            var context = modelService.CreateContext(snapshot);
            foreach (var stepHistory in _histories.Values)
            {
                stepHistory.UpdateCurrentSubmissions(snapshot);
                stepHistory.UpdateCurrentCompletion(snapshot, context, date);
            }
        }
    }

    /// <summary>
    /// Owns the complete version sequence for one step. This is the only place that mutates a version.
    /// The latest version is Current, including while frozen and waiting for this step to be reached again.
    /// </summary>
    private sealed class StepAttemptHistory(Step step, WorkflowDefinition definition, WorkflowInstanceHistory history)
    {
        private readonly List<StepVersion> _versions = [];
        private StepVersion? Current => _versions.LastOrDefault();
        public Step Step { get; } = step;

        public StepVersions GetVersions()
            => new(Current, _versions.AsEnumerable().Reverse().Skip(1).ToList());

        /// <summary>
        /// Starts a version only after progression reaches this step. First reach starts v1; frozen work starts its successor.
        /// An unfinished or completed version stays current until a rejection/reset freezes it.
        /// </summary>
        public void StartVersionWhenReached()
        {
            if (_versions.Count == 0 || _versions[^1].FrozenAt != null)
                _versions.Add(new StepVersion { VersionNumber = _versions.Count + 1 });
        }

        /// <summary>
        /// Remembers a submission in the open attempt and replaces the form's visible submission immediately.
        /// Unreached or frozen steps cannot accept work into an attempt that has not started yet.
        /// </summary>
        public void RecordSubmission(Form form, Step formStep, InstanceEventLogEntry log)
        {
            if (Current is not { FrozenAt: null } current)
                return;

            // A rejection can freeze this version before submissions are recalculated.
            current.Submissions.RemoveAll(member => member.FormName == form.Name);
            var submission =
                new StepVersionSubmission(form.Name, log.EventDate ?? log.Timestamp, [log.EventId], formStep.Name);
            current.SubmissionCandidates.Add(submission);
            current.Submissions.Add(submission);
        }

        /// <summary>
        /// Closes the current attempt at the reset timestamp, after checking its answers can be reconstructed reliably.
        /// Completion alone never freezes an attempt; later corrections still belong to it until a reset.
        /// </summary>
        public void FreezeCurrentVersionOnRestart(DateTime date)
        {
            if (Current is not { FrozenAt: null } current)
                return;
            ValidateFrozenAnswers(current, date);
            current.FrozenAt = date;
        }

        /// <summary>
        /// Removes a restarted child's work from this enclosing attempt without changing its version number.
        /// Remove candidates too, otherwise recalculation could restore submissions from the superseded child attempt.
        /// </summary>
        public void RemoveSubmissionsFrom(Step restartedStep)
        {
            var current = Current;
            current?.Submissions.RemoveAll(BelongsToRestartedStep);
            current?.SubmissionCandidates.RemoveAll(BelongsToRestartedStep);

            bool BelongsToRestartedStep(StepVersionSubmission submission)
                => submission.Step != null && IsWithin(definition.AllSteps.Get(submission.Step), restartedStep);
        }

        /// <summary>
        /// Recomputes the current version's submissions; suppression can hide them without ending the version.
        /// </summary>
        public void UpdateCurrentSubmissions(WorkflowInstance snapshot)
        {
            if (Current is not { FrozenAt: null } current)
                return;

            // Candidates retain every submission in this version. Only the latest active one per form is visible.
            // Removing suppression or deleting a newer event can make an earlier candidate visible again.
            // Match the event date as well as its ID: reusing an event ID must not revive an older submission.
            var effectiveSubmissions = current.SubmissionCandidates.Where(member => member.EventIds.Any(id =>
                    IsActive(id, snapshot, definition) && snapshot.Events[id].Date == member.DateSubmitted))
                .GroupBy(member => member.FormName).Select(group => group.Last()).ToArray();
            current.Submissions.Clear();
            current.Submissions.AddRange(effectiveSubmissions);
        }

        /// <summary>
        /// Completion can be set or cleared within the same version. Only a restart freezes the version.
        /// </summary>
        public void UpdateCurrentCompletion(WorkflowInstance snapshot, ObjectContext context, DateTime date)
        {
            if (Current is not { FrozenAt: null } current)
                return;

            if (!Step.HasEnded(context))
                current.CompletionTimestamp = null;
            else
                current.CompletionTimestamp ??= Step.GetEndDate(snapshot, definition) ?? date;
        }

        /// <summary>
        /// Rejects answer histories that may contain edits merged across this attempt's closing timestamp.
        /// Check question, condition and lookup properties because all can affect how the historical form is read.
        /// </summary>
        private void ValidateFrozenAnswers(StepVersion version, DateTime date)
        {
            foreach (var member in version.Submissions)
            {
                var form = definition.Forms.Get(member.FormName);
                var paths = form.ActualForm.PropertyDefinitions
                    .Select(question =>
                        form.PropertyName == null ? question.Name : $"{form.PropertyName}.{question.Name}")
                    .Concat(form.ActualForm.PropertyDefinitions.SelectMany(question => question.Conditions)
                        .SelectMany(condition => condition.Properties).Select(path => path.ToString()))
                    .Concat(form.Lookups.Select(path => path.ToString())).ToArray();
                if ((history.Journal?.PropertyChanges ?? []).Any(change => change.Timestamp >= date &&
                                                                           paths.Any(path =>
                                                                               PathsOverlap(path, change.Path))))
                    throw new StepVersionHistoryUnavailableException(
                        "Later answer edits may have merged across an attempt boundary; its frozen answers cannot be reconstructed exactly.");
            }
        }
    }

    /// <summary>
    /// Interprets the definition without changing history. A restart step identifies the whole subtree to reset.
    /// Suppression cycles identify possible restarts; the before/after event states identify actual restarts.
    /// </summary>
    private sealed class WorkflowVersionRules
    {
        private readonly WorkflowDefinition _definition;
        private readonly Dictionary<string, Step> _restartStepsByEvent;
        private readonly List<Step> _stepOrder;
        public Step[] VersionedSteps { get; }

        public WorkflowVersionRules(WorkflowDefinition definition)
        {
            _definition = definition;
            _restartStepsByEvent = FindRestartSteps(definition);
            _stepOrder = definition.Steps.SelectMany(Subtree).ToList();
            VersionedSteps = definition.AllSteps.Where(step =>
                step.ParentStep == null || _restartStepsByEvent.Values.Contains(step)).ToArray();
        }

        /// <summary>
        /// Returns the enclosing step to restart when this event actually invalidates earlier work in a suppression cycle.
        /// The definition identifies possible resets; comparing active events before/after establishes whether one happened.
        /// </summary>
        public Step? DetermineRestart(
            InstanceEventLogEntry log, WorkflowInstance before, WorkflowInstance snapshot)
        {
            var restartStep = _restartStepsByEvent.GetValueOrDefault(log.EventId);
            var suppressed = _definition.Events.GetOrDefault(log.EventId)?.Suppresses ?? [];
            var suppressedEventBecameInactive = suppressed.Any(id =>
                IsActive(id, before, _definition) && !IsActive(id, snapshot, _definition));
            var sourceStep = FindEventStep(_definition, log.EventId);
            // Moving backward in definition order identifies rejection/reset rather than ordinary forward progression.
            var targetsEarlierWork = restartStep != null && suppressed.Any(id =>
            {
                var targetStep = FindEventStep(_definition, id);
                if (targetStep == null)
                    return false;
                if (sourceStep != null)
                    return _stepOrder.IndexOf(targetStep) < _stepOrder.IndexOf(sourceStep);

                // A reset action without a form or completion step targets the beginning of the restarted work.
                return IsWithin(targetStep, restartStep.Children.FirstOrDefault() ?? restartStep);
            });

            // Only a rejection/reset that invalidates earlier work starts another attempt.
            return targetsEarlierWork && suppressedEventBecameInactive &&
                   log.Operation is EventLogOperation.Create or EventLogOperation.Update
                ? restartStep
                : null;
        }

        /// <summary>
        /// Locates the work represented by an event: steps it completes first, otherwise forms it submits.
        /// Use their common enclosing step when several match; the event's declaration location need not be its work's location.
        /// </summary>
        private static Step? FindEventStep(WorkflowDefinition definition, string eventId)
        {
            var completions = definition.AllSteps.Where(s => s.Ends.GetAllEventIds().Contains(eventId)).ToArray();
            if (completions.Length != 0)
                return CommonAncestor(completions);
            var forms = definition.Forms.Where(f => FormSubmissionState.GetSubmissionEventIds(f).Contains(eventId))
                .Select(f => ResolveFormStep(definition, f)).OfType<Step>().ToArray();
            return CommonAncestor(forms);
        }

        private static IEnumerable<Step> Subtree(Step step)
            => new[] { step }.Concat(step.Children.SelectMany(Subtree));

        // DraftSubmitted and RequestRevision can suppress each other, allowing repeated rounds of work.
        // A one-way Pause -> DraftSubmitted rule only hides work within the same round.
        // The common ancestor of all work cleared by a cycle is the step whose versions can restart.
        private static Dictionary<string, Step> FindRestartSteps(WorkflowDefinition definition)
        {
            var result = new Dictionary<string, Step>();
            foreach (var source in definition.Events.Where(e => e.Suppresses is { Count: > 0 }))
            {
                // Keep events reachable in both directions: each can eventually clear the other through suppression.
                var cycleEventIds = Reachable(source.Name).Where(id => Reachable(id).Contains(source.Name)).ToArray();
                if (cycleEventIds.Length < 2)
                    continue;
                // Include every outcome cleared by the cycle when finding the enclosing restart step.
                var affectedSteps = cycleEventIds.SelectMany(id => definition.Events.Get(id).Suppresses ?? [])
                    .Concat(cycleEventIds).Select(id => FindEventStep(definition, id)).OfType<Step>().ToArray();
                if (affectedSteps.Length == 0)
                    continue;
                var restartStep = CommonAncestor(affectedSteps);
                if (restartStep == null)
                    throw new StepVersionHistoryUnavailableException(
                        $"Cannot determine restart scope for event '{source.Name}'.");
                result[source.Name] = restartStep;
            }

            return result;

            // Follow suppression links transitively. The visited set prevents cycles from traversing forever.
            HashSet<string> Reachable(string start)
            {
                var found = new HashSet<string>();
                var pending = new Stack<string>([start]);
                while (pending.TryPop(out var id))
                    if (found.Add(id))
                        foreach (var target in definition.Events.GetOrDefault(id)?.Suppresses ?? [])
                            pending.Push(target);
                return found;
            }
        }
    }

    /// <summary>
    /// Rejects histories whose progression cannot be reconstructed exactly from retained property changes and condition dates.
    /// Merged edits can obscure when a prerequisite changed; arbitrary changes from expressions reading now cannot be scheduled.
    /// </summary>
    private static void ValidateProgressionHistory(WorkflowDefinition definition, WorkflowInstanceHistory history)
    {
        // shortcut: merged edits have no provenance; widen support when history records reliable attempt boundaries.
        var progressionPaths = definition.AllSteps.SelectMany(step =>
                (step.Condition?.Properties ?? []).Concat(step.Ends?.Properties ?? [])).Select(path => path.ToString())
            .ToArray();
        if (progressionPaths.Contains("now") || (history.Journal?.PropertyChanges ?? []).Any(change =>
                progressionPaths.Any(path => PathsOverlap(path, change.Path))))
            throw new StepVersionHistoryUnavailableException(
                "Property-dependent progression cannot be reconstructed exactly from a journal that may contain merged edits.");
    }

    private static bool PathsOverlap(string left, string right)
        => left == right || left.StartsWith(right + ".", StringComparison.Ordinal) ||
           right.StartsWith(left + ".", StringComparison.Ordinal);

    // An event contributes only if it has a date and no later event suppresses it.
    private static bool IsActive(string id, WorkflowInstance instance, WorkflowDefinition definition)
        => instance.Events.GetValueOrDefault(id)?.Date != null &&
           EventSuppressionHelper.IsEventActive(id, instance, definition);

    // The nearest shared enclosing step defines the smallest subtree containing all this work.
    private static Step? CommonAncestor(Step[] steps)
        => steps.Length == 0
            ? null
            : Ancestors(steps[0]).FirstOrDefault(candidate => steps.All(s => IsWithin(s, candidate)));

    // Include the subtree's root itself, so resetting a step also resets its own attempt.
    private static bool IsWithin(Step step, Step ancestor) => Ancestors(step).Contains(ancestor);

    // Visit the step itself before its parents, nearest first; grouping and common-ancestor lookup depend on this order.
    private static IEnumerable<Step> Ancestors(Step step)
    {
        for (Step? current = step; current != null; current = current.ParentStep)
            yield return current;
    }
}