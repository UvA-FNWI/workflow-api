using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.Users.Dtos;
using UvA.Workflow.Events;
using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public class InstanceHistoryDtoFactory(
    ModelService modelService,
    WorkflowInstanceService workflowInstanceService,
    IUserService userService,
    IUserRepository userRepository)
{
    private record PropertyEdit(PropertyDefinition Question, string Path, AnswerChangeGroup Group, AnswerChange Change);

    public async Task<InstanceHistoryItemDto[]> Create(WorkflowInstance instance, CancellationToken ct)
    {
        var history = await workflowInstanceService.GetInstanceHistory(instance.Id, ct);
        var workflowDefinition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];

        var eventUsers = (await userRepository.GetByIds(
                history.EventLogs.Select(log => log.ExecutedBy).Distinct().ToList(), ct))
            .ToDictionary(user => user.Id);

        var propertyEdits = GetPropertyEdits(instance, workflowDefinition, history);
        var journalUsers = await userService.GetUsers(
            propertyEdits.SelectMany(edit => edit.Group.Changes)
                .Select(change => change.ChangedBy)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(), ct);

        var eventEntries = history.EventLogs
            .Where(log => log.Operation is EventLogOperation.Create or EventLogOperation.Update
                or EventLogOperation.Undo)
            .Select(log => new InstanceHistoryItemDto(
                log.Id,
                log.Operation == EventLogOperation.Undo ? InstanceHistoryType.Undo : InstanceHistoryType.Event,
                log.Operation == EventLogOperation.Undo
                    ? GetUndoTitle(workflowDefinition, log, history.EventLogs)
                    : workflowDefinition.Events.Find(e => e.Name == log.EventId)?.Label ?? log.EventId,
                log.Timestamp,
                CreateUserDto(eventUsers.GetValueOrDefault(log.ExecutedBy), log.ExecutedBy),
                null,
                UndoHistory: log.Operation == EventLogOperation.Undo
                    ? new UndoHistoryDto(log.Reason)
                    : null));

        var propertyEntries = propertyEdits.Select(edit => new InstanceHistoryItemDto(
            $"{edit.Path}:{edit.Change.ChangedAt.Ticks}",
            InstanceHistoryType.Property,
            new BilingualString(
                $"{edit.Question.DisplayName.En} changed",
                $"{edit.Question.DisplayName.Nl} aangepast"
            ),
            edit.Change.ChangedAt,
            CreateUserDto(journalUsers.GetValueOrDefault(edit.Change.ChangedBy!), edit.Change.ChangedBy!),
            new PropertyHistoryDto(
                QuestionDto.Create(edit.Question, totalWeight: 0),
                CreateGroupDto(edit.Question, edit.Group, journalUsers)),
            null));

        return eventEntries.Concat(propertyEntries)
            .OrderByDescending(entry => entry.Timestamp)
            .ToArray();
    }

    private static PropertyEdit[] GetPropertyEdits(WorkflowInstance instance, WorkflowDefinition workflowDefinition,
        WorkflowInstanceHistory history)
    {
        if (history.Journal?.PropertyChanges is not { Length: > 0 } journal)
            return [];

        var changedPaths = journal.Select(change => change.Path).ToHashSet();

        return workflowDefinition.Forms
            .SelectMany(form => form.ActualForm.PropertyDefinitions.Select(question => new
            {
                Form = form,
                Question = question,
                Path = form.PropertyName == null ? question.Name : $"{form.PropertyName}.{question.Name}"
            }))
            .Where(item => changedPaths.Contains(item.Path))
            .SelectMany(item => AnswerChangeHistory.For(
                    journal,
                    item.Question.Name,
                    item.Form.PropertyName,
                    FormSubmissionState.GetSubmissionEventIds(item.Form),
                    history.EventLogs,
                    workflowDefinition,
                    instance.GetProperty(item.Form.PropertyName, item.Question.Name))
                .SelectMany(group => group.Changes
                    // ChangedBy is null for the "submitted value" row; only real edits are history entries.
                    .Where(change => change.ChangedBy != null)
                    .Select(change => new PropertyEdit(item.Question, item.Path, group, change))))
            // Derived forms share questions, so the same edit can be found more than once.
            .DistinctBy(edit => (edit.Path, edit.Change.ChangedAt))
            .ToArray();
    }

    private static BilingualString GetUndoTitle(WorkflowDefinition workflowDefinition, InstanceEventLogEntry undo,
        IEnumerable<InstanceEventLogEntry> eventLogs)
    {
        var operation = eventLogs.FirstOrDefault(log => log.OperationMetadata?.Id == undo.OperationId)
            ?.OperationMetadata;
        var label = operation?.Type switch
        {
            OperationType.FormSubmission =>
                workflowDefinition.Forms.GetOrDefault(operation.Source)?.DisplayName ?? operation.Source,
            OperationType.ExecuteAction =>
                workflowDefinition.AllActions.FirstOrDefault(a => a.Name == operation.Source)?.Label
                ?? operation.Source,
            _ => undo.OperationId ?? undo.Id
        };

        return new BilingualString(
            $"{label.En} reversed",
            $"{label.Nl} teruggedraaid"
        );
    }

    private static AnswerChangeGroupDto CreateGroupDto(PropertyDefinition question, AnswerChangeGroup group,
        IReadOnlyDictionary<string, User> users)
        => new(group.VersionNumber, group.Changes.Select(change => new AnswerChangeDto(
            Answer.GetValue(question, change.Value),
            change.ChangedAt,
            change.ChangedBy == null
                ? null
                : users.GetValueOrDefault(change.ChangedBy)?.DisplayName ?? change.ChangedBy)).ToArray());

    private static UserSearchResultDto CreateUserDto(User? user, string fallback)
        => user == null
            ? new(fallback, fallback, "", UserSearchSources.Repository, null, false)
            : new(user.UserName, user.DisplayName, user.Email, UserSearchSources.Repository, user.Organization,
                UserProviderKeys.IsExternal(user.ProviderKey));
}