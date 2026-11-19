using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Submissions.Dtos;

public record PreviewableFormDto(string Name, BilingualString Title, BilingualString? Label);

public record FormPreviewRequest([Required] Dictionary<string, JsonElement?> Answers);

public record FormPreviewDto(
    string FormName,
    FormDto Form,
    AnswerDto[] Answers,
    Dictionary<string, ChoiceDto[]> ReferenceChoices,
    PreviewPageResult[] PageResults)
{
    public static FormPreviewDto Create(FormPreview preview) => new(
        preview.Form.Name,
        FormDto.Create(preview.Form, preview.Context),
        preview.Answers.Select(answer => new AnswerDto(answer.Id, answer.QuestionName, answer.FormName,
            answer.WorkflowDefinition, answer.IsVisible, answer.ValidationError, answer.Value,
            VisibleChoices: answer.VisibleChoices)).ToArray(),
        preview.ReferenceChoices.ToDictionary(entry => entry.Key,
            entry => entry.Value.Select(choice => new ChoiceDto(choice.Name, choice.Text ?? choice.Name,
                choice.Description, choice.Value)).ToArray()),
        preview.PageResults);
}