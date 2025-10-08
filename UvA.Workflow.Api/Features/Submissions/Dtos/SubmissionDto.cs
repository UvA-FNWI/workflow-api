namespace UvA.Workflow.Api.Features.Submissions.Dtos;

public record AnswerFile(string Id, string Name, string? Url);

public record Answer(
    string Id,
    string QuestionName,
    string FormName,
    string EntityType,
    bool IsVisible,
    BilingualString? ValidationError = null,
    string? Text = null,
    DateTime? DateTime = null,
    double? Number = null,
    ExternalUser? User = null,
    AnswerFile[]? Files = null,
    string[]? VisibleChoices = null
) : Value(Text, DateTime, Number, Files)
{
    public static Answer[] FromEntities(WorkflowInstance inst, Form form,
        Dictionary<string, QuestionStatus> questions, FileService? fileService = null)
        => questions.Select(e => e.Value.IsVisible
            ? FromEntity(form, e.Key, inst.GetProperty(form.Property, e.Key),
                    validationError: e.Value.ValidationError, fileService: fileService)
                with
                {
                    VisibleChoices = e.Value.Choices
                }
            : FromEntity(form, e.Key, isVisible: false, fileService: fileService)
        ).ToArray();

    private static Answer FromEntity(Form form, string questionName,
        BsonValue? answer = null, bool isVisible = true,
        BilingualString? validationError = null, FileService? fileService = null)
    {
        var entityType = form.ActualForm.EntityType.Name;
        var question = form.ActualForm.EntityType.Properties[questionName];

        if (question.DataType == DataType.Currency) // TODO: this is an odd one
        {
            var value = ObjectContext.GetValue(answer, question) as CurrencyAmount;
            return new Answer($"{form.Name}_{questionName}", questionName, form.Name, entityType, isVisible,
                validationError,
                Text: value?.Currency, Number: value?.Amount);
        }

        if (question.DataType == DataType.User && question.IsArray)
            return new Answer($"{form.Name}_{questionName}", questionName, form.Name, entityType, isVisible,
                validationError,
                Text: (ObjectContext.GetValue(answer, question) as User[])?.Select(u => u.ToExternalUser())
                .Serialize());

        if (question.DataType == DataType.User)
            return new Answer($"{form.Name}_{questionName}", questionName, form.Name, entityType, isVisible,
                validationError,
                User: (ObjectContext.GetValue(answer, question) as User)?.ToExternalUser());

        if (question.DataType == DataType.File)
        {
            var value = ObjectContext.GetValue(answer, question) as StoredFile;
            return new Answer($"{form.Name}_{questionName}", questionName, form.Name, entityType, isVisible,
                validationError,
                Text: value?.FileName,
                Files: value == null
                    ? []
                    : [new AnswerFile(value.Id.ToString(), value.FileName, fileService?.GenerateUrl(value))]
            );
        }

        return new Answer($"{form.Name}_{questionName}", questionName, form.Name, entityType, isVisible,
            validationError,
            answer?.IsString == true || answer?.IsBsonDocument == true ? answer.ToString() : null,
            answer?.IsBsonDateTime == true ? answer.ToLocalTime() : null,
            (answer?.IsDouble == true || answer?.IsInt32 == true)
                ? answer.ToDouble()
                : null // TODO fix int handling properly
        );
    }

    // public Question GetQuestion(ModelService modelService)
    //     => Question.FromModel(modelService.EntityTypes[EntityType].Properties[QuestionName]);
    //
    // public Task<Message[]> GetMessages() // MessagesByObjectIdDataLoader loader
    //     => Task.FromResult<Message[]>([]); // TODO: loader.LoadAsync(new MessageId(TargetType.Answer, Id));
}

public record Value(
    string? Text = null,
    DateTime? DateTime = null,
    double? Number = null,
    AnswerFile[]? Files = null,
    BilingualString? LocalText = null,
    string? Href = null
)
{
    public static Value FromObject(object? value) => value switch
    {
        string s => new Value(s),
        DateTime d => new Value(DateTime: d),
        CurrencyAmount c => new Value(Text: c.Currency, Number: c.Amount),
        double d => new Value(Number: d),
        int i => new Value(Number: i),
        BilingualString s => new Value(LocalText: s),
        object[] arr => new Value(arr.Serialize()),
        _ => new Value()
    };
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
            shownQuestionIds == null ? [] : Answer.FromEntities(inst, form, shownQuestionIds, fileService),
            sub?.Date,
            WorkflowInstanceDto.From(inst)
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