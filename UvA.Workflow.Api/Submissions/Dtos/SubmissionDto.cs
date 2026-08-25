using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Journaling;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Submissions.Dtos;

public record SubmissionDto(
    string Id,
    string FormName,
    string InstanceId,
    AnswerDto[] Answers,
    DateTime? DateSubmitted,
    FormDto Form,
    RoleAction[] Permissions);

public class SubmissionDtoFactory(
    ArtifactTokenService artifactTokenService,
    ModelService modelService,
    IUserService? userService = null)
{
    private readonly AnswerDtoFactory _answerDtoFactory = new(artifactTokenService);

    public async Task<SubmissionDto> CreateAsync(WorkflowInstance inst, Form form,
        FormSubmissionState submissionState,
        Dictionary<string, QuestionStatus>? shownQuestionIds = null, RoleAction[]? permissions = null,
        InstanceJournalEntry? journal = null, CancellationToken ct = default)
    {
        var displayNames = await ResolveDisplayNames(journal, ct);
        return Create(inst, form, submissionState, shownQuestionIds, permissions, journal, displayNames);
    }

    public SubmissionDto Create(WorkflowInstance inst, Form form, FormSubmissionState submissionState,
        Dictionary<string, QuestionStatus>? shownQuestionIds = null, RoleAction[]? permissions = null,
        InstanceJournalEntry? journal = null, IReadOnlyDictionary<string, string>? displayNames = null)
    {
        var context = modelService.CreateContext(inst);
        var answers = shownQuestionIds == null ? [] : Answer.Create(inst, form, shownQuestionIds);
        return new(form.Name,
            form.Name,
            inst.Id,
            answers.Select(a =>
                    _answerDtoFactory.Create(a,
                        a.IsVisible ? CreateChanges(inst, form, a, submissionState, journal, displayNames) : null))
                .ToArray(),
            submissionState.DateSubmitted,
            FormDto.Create(form, context),
            permissions ?? []
        );
    }

    public async Task<IReadOnlyDictionary<string, string>> ResolveDisplayNames(InstanceJournalEntry? journal,
        CancellationToken ct)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (userService == null || journal?.PropertyChanges is not { Length: > 0 })
            return names;

        var userNames = journal.PropertyChanges.Select(c => c.ModifiedBy)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var users = await userService.GetUsers(userNames, ct);
        foreach (var userName in userNames)
            names[userName] = users.TryGetValue(userName, out var user) && !string.IsNullOrWhiteSpace(user.DisplayName)
                ? user.DisplayName
                : userName;

        return names;
    }

    private AnswerChangeDto[]? CreateChanges(WorkflowInstance inst, Form form, Answer answer,
        FormSubmissionState submissionState, InstanceJournalEntry? journal,
        IReadOnlyDictionary<string, string>? displayNames)
    {
        if (journal?.PropertyChanges is not { Length: > 0 })
            return null;

        var history = AnswerChangeHistory.For(
            journal.PropertyChanges,
            answer.QuestionName,
            form.PropertyName,
            submissionState.DateSubmitted,
            inst.GetProperty(form.PropertyName, answer.QuestionName));
        if (history.Length == 0)
            return null;

        var question = modelService.GetProperty(inst, form.PropertyName, answer.QuestionName);
        return history.Select(change => new AnswerChangeDto(
            change.Version,
            question == null ? null : Answer.GetValue(question, change.Value),
            change.ChangedAt,
            change.ChangedBy == null
                ? null
                : displayNames?.GetValueOrDefault(change.ChangedBy) ?? change.ChangedBy)).ToArray();
    }
}