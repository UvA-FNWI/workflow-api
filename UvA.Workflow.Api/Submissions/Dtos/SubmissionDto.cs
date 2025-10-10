using System.Text.Json;
using UvA.Workflow.Api.WorkflowInstances.Dtos;

namespace UvA.Workflow.Api.Submissions.Dtos;

public record AnswerFile(string Id, string Name, string? Url);

public record Answer(
    string Id,
    string QuestionName,
    string FormName,
    string EntityType,
    bool IsVisible,
    BilingualString? ValidationError = null,
    JsonElement? Value = null,
    AnswerFile[]? Files = null,
    string[]? VisibleChoices = null
)
{
    public static Answer[] Create(WorkflowInstance inst, Form form,
        Dictionary<string, QuestionStatus> questions, FileService? fileService = null)
        => questions.Select(e => e.Value.IsVisible
            ? Create(form, e.Key, inst.GetProperty(form.Property, e.Key),
                    validationError: e.Value.ValidationError, fileService: fileService)
                with
                {
                    VisibleChoices = e.Value.Choices
                }
            : Create(form, e.Key, isVisible: false, fileService: fileService)
        ).ToArray();

    private static Answer Create(Form form, string questionName,
        BsonValue? answer = null, bool isVisible = true,
        BilingualString? validationError = null, FileService? fileService = null)
    {
        var entityType = form.ActualForm.EntityType.Name;
        var question = form.ActualForm.EntityType.Properties[questionName];

        if (question.DataType == DataType.Currency)
        {
            var value = ObjectContext.GetValue(answer, question) as CurrencyAmount;
            var currencyObj = new { currency = value?.Currency, amount = value?.Amount };
            return new Answer($"{form.Name}_{questionName}", questionName, form.Name, entityType, isVisible,
                validationError,
                Value: value == null ? null : JsonSerializer.SerializeToElement(currencyObj));
        }

        if (question.DataType == DataType.User && question.IsArray)
        {
            var users = (ObjectContext.GetValue(answer, question) as User[])?.Select(u => u.ToExternalUser()).ToArray();
            return new Answer($"{form.Name}_{questionName}", questionName, form.Name, entityType, isVisible,
                validationError,
                Value: users == null ? null : JsonSerializer.SerializeToElement(users));
        }

        if (question.DataType == DataType.User)
        {
            var user = (ObjectContext.GetValue(answer, question) as User)?.ToExternalUser();
            return new Answer($"{form.Name}_{questionName}", questionName, form.Name, entityType, isVisible,
                validationError,
                Value: user == null ? null : JsonSerializer.SerializeToElement(user));
        }

        if (question.DataType == DataType.File)
        {
            var value = ObjectContext.GetValue(answer, question) as StoredFile;
            return new Answer($"{form.Name}_{questionName}", questionName, form.Name, entityType, isVisible,
                validationError,
                Value: value?.FileName == null ? null : JsonSerializer.SerializeToElement(value.FileName),
                Files: value == null
                    ? []
                    : [new AnswerFile(value.Id.ToString(), value.FileName, fileService?.GenerateUrl(value))]
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

public record Value(
    JsonElement? Data = null,
    AnswerFile[]? Files = null
)
{
    public static Value FromObject(object? obj) => new(
        Data: obj == null ? null : JsonSerializer.SerializeToElement(obj)
    );

    // Helper methods to extract typed values (requires Question.DataType for type safety)
    public string? AsString() => Data?.ValueKind == JsonValueKind.String ? Data.Value.GetString() : null;

    public DateTime? AsDateTime() =>
        Data?.ValueKind == JsonValueKind.String && DateTime.TryParse(Data.Value.GetString(), out var dt) ? dt : null;

    public double? AsNumber() => Data?.ValueKind == JsonValueKind.Number ? Data.Value.GetDouble() : null;
    public int? AsInt() => Data?.ValueKind == JsonValueKind.Number ? Data.Value.GetInt32() : null;

    public T? As<T>() where T : class
    {
        if (Data == null) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(Data.Value);
        }
        catch
        {
            return null;
        }
    }
}

public record SubmissionDto(
    string Id,
    string FormName,
    string InstanceId,
    Answer[] Answers,
    DateTime? DateSubmitted,
    WorkflowInstanceDto WorkflowInstance)
{
    public static SubmissionDto FromEntity(WorkflowInstance inst,
        Form form,
        InstanceEvent? sub,
        Dictionary<string, QuestionStatus>? shownQuestionIds = null,
        FileService? fileService = null
    )
        => new(form.Name,
            form.Name,
            inst.Id,
            shownQuestionIds == null ? [] : Answer.Create(inst, form, shownQuestionIds, fileService),
            sub?.Date,
            WorkflowInstanceDto.Create(inst)
        );
}

public record InvalidQuestion(
    string QuestionName,
    BilingualString ValidationMessage);

public record SubmitSubmissionResult(
    SubmissionDto Submission,
    WorkflowInstanceDto? UpdatedInstance = null,
    InvalidQuestion[]? ValidationErrors = null,
    bool Success = true);