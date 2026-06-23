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
        var file = ObjectContext.GetValue(answer, question) as ArtifactInfo;

        return new Answer($"{form.Name}_{questionName}", questionName, form.Name, workflowDefinition, isVisible,
            validationError, value, question.DataType == DataType.File ? file != null ? [file] : [] : null);
    }

    public static JsonElement? GetValue(PropertyDefinition question, BsonValue? answer)
    {
        if (question.DataType == DataType.Currency)
        {
            var value = ObjectContext.GetValue(answer, question) as CurrencyAmount;
            var currencyObj = new { currency = value?.Currency, amount = value?.Amount };
            return value == null
                ? null
                : JsonSerializer.SerializeToElement(currencyObj, AnswerConversionService.Options);
        }

        if (question.DataType == DataType.User && question.IsArray)
        {
            var users = ObjectContext.GetValue(answer, question) as InstanceUser[];
            return users == null
                ? null
                : JsonSerializer.SerializeToElement(users, AnswerConversionService.Options);
        }

        if (question.DataType == DataType.User)
        {
            var user = ObjectContext.GetValue(answer, question) as InstanceUser;
            return user == null ? null : JsonSerializer.SerializeToElement(user, AnswerConversionService.Options);
        }

        if (question.DataType == DataType.File)
        {
            var value = ObjectContext.GetValue(answer, question) as ArtifactInfo;
            return value?.Name == null ? null : JsonSerializer.SerializeToElement(value.Name);
        }

        if (question.DataType == DataType.Boolean)
        {
            // Default to false, not to null
            var value = ObjectContext.GetValue(answer, question) as bool?;
            return JsonSerializer.SerializeToElement(value ?? false);
        }

        // Handle remaining types: String, DateTime, Date, Int, Double, Choice, Reference
        var convertedValue = ObjectContext.GetValue(answer, question);
        return convertedValue == null ? null : JsonSerializer.SerializeToElement(convertedValue);
    }
}