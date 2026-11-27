using System.Diagnostics.CodeAnalysis;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Infrastructure;

internal sealed class WorkflowLocalization(WorkflowDefinition workflowDefinition)
{
    private readonly IReadOnlyList<string> _languages = workflowDefinition.EffectiveLanguages;

    [return: NotNullIfNotNull(nameof(text))]
    public BilingualString? Resolve(BilingualString? text) => BilingualString.Resolve(text, _languages);

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

    private QuestionDto Apply(QuestionDto question)
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