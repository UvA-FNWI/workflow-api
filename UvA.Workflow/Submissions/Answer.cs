using System.Text.Json;
using UvA.Workflow.Persistence;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Submissions;

public record Answer(
    string Id,
    string QuestionName,
    string FormName,
    string WorkflowDefinition,
    bool IsVisible,
    BilingualString? ValidationError = null,
    JsonElement? Value = null,
    ArtifactInfo[]? Files = null,
    string[]? VisibleChoices = null
)
{
    public static Answer[] Create(WorkflowInstance inst, Form form,
        Dictionary<string, QuestionStatus> questions)
        => questions.Select(e => e.Value.IsVisible
            ? Create(form, e.Key, inst.GetProperty(form.PropertyName, e.Key),
                    validationError: e.Value.ValidationError)
                with
                {
                    VisibleChoices = e.Value.Choices
                }
            : Create(form, e.Key, isVisible: false)
        ).ToArray();

    private static Answer Create(Form form, string questionName,
        BsonValue? answer = null, bool isVisible = true,
        BilingualString? validationError = null)
    {
        var workflowDefinition = form.ActualForm.WorkflowDefinition.Name;

        if (!isVisible)
            return new Answer($"{form.Name}_{questionName}", questionName, form.Name, workflowDefinition,
                false);

        var question = form.ActualForm.WorkflowDefinition.Properties.Get(questionName);
        var value = GetValue(question, answer);
        var files = question.DataType == DataType.File
            ? question.IsArray
                ? ObjectContext.GetValue(answer, question) as ArtifactInfo[] ?? []
                : ObjectContext.GetValue(answer, question) is ArtifactInfo file
                    ? new[] { file }
                    : []
            : null;

        return new Answer($"{form.Name}_{questionName}", questionName, form.Name, workflowDefinition, isVisible,
            validationError, value, files);
    }

    public static JsonElement? GetValue(PropertyDefinition question, BsonValue? answer)
    {
        if (question is not ObjectPropertyDefinition || answer == null || answer.IsBsonNull)
            return SerializeValue(question, ObjectContext.GetValue(answer, question));

        if (question.IsArray && !answer.IsBsonArray)
            answer = new BsonArray { answer };
        return SerializeValue(question,
            BsonConversionTools.ConvertBasicBsonValue(BsonConversionTools.GetStoredValue(answer, question)));
    }

    public static JsonElement? SerializeValue(PropertyDefinition question, object? value)
    {
        value = question.DataType switch
        {
            DataType.Currency => value is CurrencyAmount currency
                ? new { currency = currency.Currency, amount = currency.Amount } : null,
            DataType.User when question.IsArray => value as InstanceUser[],
            DataType.User => value as InstanceUser,
            DataType.File when question.IsArray => value is ArtifactInfo[] { Length: > 0 } files
                ? files.Select(file => file.Name).ToArray() : null,
            DataType.File => (value as ArtifactInfo)?.Name,
            DataType.Check => value as bool? ?? false,
            DataType.Reference when value is Dictionary<Lookup, object?> reference => reference.GetValueOrDefault("Id"),
            DataType.Reference when value is Dictionary<Lookup, object?>?[] references
                => references.Select(reference => reference?.GetValueOrDefault("Id")).ToArray(),
            _ => value
        };
        var options = question.DataType is DataType.Currency or DataType.User ? AnswerConversionService.Options : null;
        return value == null ? null : JsonSerializer.SerializeToElement(value, options);
    }
}
