using System.Text.Json;
using UvA.Workflow.Persistence;

namespace UvA.Workflow.Submissions;

public record Answer(
    string Id,
    string QuestionName,
    string FormName,
    string EntityType,
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
            ? Create(form, e.Key, inst.GetProperty(form.Property, e.Key),
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
        var entityType = form.ActualForm.EntityType.Name;
        var question = form.ActualForm.EntityType.Properties[questionName];

        if (question.DataType == DataType.Currency)
        {
            var value = ObjectContext.GetValue(answer, question) as CurrencyAmount;
            var currencyObj = new { currency = value?.Currency, amount = value?.Amount };
            return new Answer($"{form.Name}_{questionName}", questionName, form.Name, entityType, isVisible,
                validationError,
                Value: value == null
                    ? null
                    : JsonSerializer.SerializeToElement(currencyObj, AnswerConversionService.Options));
        }

        if (question.DataType == DataType.User && question.IsArray)
        {
            var users = (ObjectContext.GetValue(answer, question) as User[])?.Select(u => u.ToExternalUser()).ToArray();
            return new Answer($"{form.Name}_{questionName}", questionName, form.Name, entityType, isVisible,
                validationError,
                Value: users == null
                    ? null
                    : JsonSerializer.SerializeToElement(users, AnswerConversionService.Options));
        }

        if (question.DataType == DataType.User)
        {
            var user = (ObjectContext.GetValue(answer, question) as User)?.ToExternalUser();
            return new Answer($"{form.Name}_{questionName}", questionName, form.Name, entityType, isVisible,
                validationError,
                Value: user == null ? null : JsonSerializer.SerializeToElement(user, AnswerConversionService.Options));
        }

        if (question.DataType == DataType.File)
        {
            var value = ObjectContext.GetValue(answer, question) as ArtifactInfo;
            return new Answer($"{form.Name}_{questionName}", questionName, form.Name, entityType, isVisible,
                validationError,
                Value: value?.Name == null ? null : JsonSerializer.SerializeToElement(value.Name),
                Files: value == null
                    ? []
                    : [value]
            );
        }

        // Handle remaining types: String, DateTime, Date, Int, Double, Choice, Reference
        var convertedValue = ObjectContext.GetValue(answer, question);
        return new Answer($"{form.Name}_{questionName}", questionName, form.Name, entityType, isVisible,
            validationError,
            Value: convertedValue == null ? null : JsonSerializer.SerializeToElement(convertedValue));
    }

    // public Question GetQuestion(ModelService modelService)
    //     => Question.FromModel(modelService.EntityTypes[EntityType].Properties[QuestionName]);
    //
    // public Task<Message[]> GetMessages() // MessagesByObjectIdDataLoader loader
    //     => Task.FromResult<Message[]>([]); // TODO: loader.LoadAsync(new MessageId(TargetType.Answer, Id));
}