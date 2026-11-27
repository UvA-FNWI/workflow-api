using System.Diagnostics.CodeAnalysis;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Infrastructure;

internal sealed class WorkflowLocalization(WorkflowDefinition workflowDefinition)
{
    private readonly IReadOnlyList<string> _languages = workflowDefinition.EffectiveLanguages;

    [return: NotNullIfNotNull(nameof(text))]
    public BilingualString? Resolve(BilingualString? text) => BilingualString.Resolve(text, _languages);

    public static BilingualString WithFallback(BilingualString text) =>
        BilingualString.Resolve(text, WorkflowDefinition.SupportedLanguages);

    [return: NotNullIfNotNull(nameof(value))]
    public object? ResolveValue(object? value) => value switch
    {
        BilingualString text => Resolve(text),
        BilingualString[] texts => texts.Select(Resolve).ToArray(),
        object[] items when items.GetType() == typeof(object[]) => items.Select(ResolveValue).ToArray(),
        _ => value
    };

    public WorkflowInstanceDto Apply(WorkflowInstanceDto instance) => instance with
    {
        Actions = instance.Actions.Select(Apply).ToArray(),
        PinnedActions = instance.PinnedActions.Select(Apply).ToArray(),
        Fields = instance.Fields.Select(field => field with
        {
            Title = Resolve(field.Title), Value = ResolveValue(field.Value)
        }).ToArray(),
        Steps = instance.Steps.Select(Apply).ToArray(),
        InfoCards = instance.InfoCards.Select(Apply).ToArray(),
        StopMessage = Resolve(instance.StopMessage)
    };

    public FormDto Apply(FormDto form) => form with
    {
        Title = Resolve(form.Title),
        Pages = form.Pages.Select(Apply).ToArray(),
        Confirmation = form.Confirmation is { } confirmation
            ? confirmation with { Title = Resolve(confirmation.Title), Text = Resolve(confirmation.Text) }
            : null
    };

    public AnswerDto Apply(AnswerDto answer) => answer with { ValidationError = Resolve(answer.ValidationError) };

    public Answer Apply(Answer answer) => answer with { ValidationError = Resolve(answer.ValidationError) };

    public InvalidQuestion Apply(InvalidQuestion error) => error with
    {
        ValidationMessage = Resolve(error.ValidationMessage)
    };

    [return: NotNullIfNotNull(nameof(result))]
    public EffectResult? Apply(EffectResult? result) => result == null
        ? null
        : result with
        {
            Toast = result.Toast is { } toast ? toast with { Message = Resolve(toast.Message) } : null,
            Error = result.Error is { } error ? error with { Message = Resolve(error.Message) } : null
        };

    private ActionDto Apply(ActionDto action) => action with { Title = Resolve(action.Title) };

    private StepDto Apply(StepDto step) => step with
    {
        Title = Resolve(step.Title),
        Children = step.Children?.Select(Apply).ToArray(),
        HeaderStatus = step.HeaderStatus is { } header ? header with { Label = Resolve(header.Label) } : null,
        Deadline = step.Deadline is { } deadline ? deadline with { Message = Resolve(deadline.Message) } : null,
        UndoCandidate = step.UndoCandidate is { } undo
            ? undo with { StepTitle = Resolve(undo.StepTitle), SourceTitle = Resolve(undo.SourceTitle) }
            : null
    };

    private InfoCardDto Apply(InfoCardDto card) => card with
    {
        Title = Resolve(card.Title),
        EmptyText = Resolve(card.EmptyText),
        Content = Resolve(card.Content),
        Fields = card.Fields?.Select(field => field with
        {
            Title = Resolve(field.Title), Value = ResolveValue(field.Value)
        }).ToArray(),
        Groups = card.Groups?.Select(group => group with
        {
            Title = Resolve(group.Title),
            UserRoles = group.UserRoles.Select(role => role with { Title = Resolve(role.Title) }).ToArray()
        }).ToArray(),
        Items = card.Items?.Select(item => item with { Text = Resolve(item.Text), Url = Resolve(item.Url) }).ToArray()
    };

    private PageDto Apply(PageDto page) => page with
    {
        Title = Resolve(page.Title),
        Elements = page.Elements.Select(Apply).ToArray()
    };

    private PageElementDto Apply(PageElementDto element) => element with
    {
        Question = element.Question == null ? null : Apply(element.Question),
        Callout = element.Callout is { } callout
            ? callout with { Title = Resolve(callout.Title), Text = Resolve(callout.Text) }
            : null,
        Text = Resolve(element.Text)
    };

    public QuestionDto Apply(QuestionDto question)
    {
        var localized = question with
        {
            Text = Resolve(question.Text),
            ShortText = Resolve(question.ShortText),
            Description = Resolve(question.Description)
        };

        return localized switch
        {
            ChoiceQuestionDto choice => choice with
            {
                Choices = choice.Choices.Select(Apply).ToArray(),
                Rubric = choice.Rubric?.Select(Apply).ToList()
            },
            ObjectQuestionDto embedded => embedded with
            {
                SubProperties = embedded.SubProperties?.Select(Apply).ToArray()
            },
            _ => localized
        };
    }

    private ChoiceDto Apply(ChoiceDto choice) => choice with
    {
        Text = Resolve(choice.Text),
        Description = Resolve(choice.Description)
    };

    private RubricEntryDto Apply(RubricEntryDto entry) => entry with
    {
        Description = Resolve(entry.Description),
        Grades = entry.Grades.Select(grade => grade with { Text = Resolve(grade.Text) }).ToList()
    };
}