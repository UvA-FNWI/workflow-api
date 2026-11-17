using System.Text.Json;
using MongoDB.Bson;
using Moq;
using MongoDB.Driver;
using UvA.Workflow.Tools;
using UvA.Workflow.Assessments;
using UvA.Workflow.Submissions;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests;

public class FormPreviewTests
{
    [Theory]
    [InlineData("{}", 0, 1, null)]
    [InlineData("{\"Score\":0}", 1, 1, 0d)]
    [InlineData("{\"Score\":2,\"ShowExtra\":true}", 1, 2, null)]
    [InlineData("{\"Score\":2,\"SecondScore\":8,\"ShowExtra\":true}", 2, 2, 5d)]
    [InlineData("{\"Score\":2,\"SecondScore\":8,\"ShowExtra\":false}", 1, 1, 5d)]
    [InlineData("{\"Score\":null,\"ShowExtra\":true}", 0, 2, null)]
    public async Task PreviewReportsVisibleCalculationProgressWithoutTreatingMissingAnswersAsZero(
        string input, int answered, int total, double? average)
    {
        var (service, instance, model) = Create();
        var definition = model.WorkflowDefinitions["Project"];
        var second = new DoublePropertyDefinition
        {
            Name = "SecondScore", Type = "Double", ParentType = definition,
            Calculation = new() { Type = CalculationType.Average, Weight = 1 },
            Condition = new() { Value = new() { Property = "ShowExtra", Equal = "true" } }
        };
        definition.Properties.Add(second);
        definition.Forms.Single(f => f.Name == "Review").Pages[0].PageElements =
        [
            .. definition.Forms.Single(f => f.Name == "Review").Pages[0].PageElements,
            new() { Question = second.Name, QuestionDefinition = second }
        ];
        instance.Properties["Score"] = 9;

        var result = await service.Evaluate(instance, "Review", Values(input), default);
        var json = JsonSerializer.SerializeToElement(
            UvA.Workflow.Api.Submissions.Dtos.FormPreviewDto.Create(result),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var page = json.GetProperty("pageResults")[0];

        Assert.Equal(answered, page.GetProperty("answeredCount").GetInt32());
        Assert.Equal(total, page.GetProperty("questionCount").GetInt32());
        Assert.Equal(average, page.GetProperty("weightedAverage").ValueKind == JsonValueKind.Null
            ? null
            : page.GetProperty("weightedAverage").GetDouble());
        Assert.Equal(9, instance.Properties["Score"].AsInt32);
        if (average != null)
            Assert.Equal(AssessmentHelpers.CalculateSourceResult(
                    definition.Forms.Single(form => form.Name == "Review"), result.Context, null)
                .PageResults.Single().WeightedAverage, result.PageResults.Single().WeightedAverage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ADisplayedReplacementDoesNotGrantAccessToTheUnderlyingProperty(bool infoCard)
    {
        var (service, instance, model) = Create();
        var definition = model.WorkflowDefinitions["Project"];
        instance.Properties["WorkingTitle"] = "Unrevealed value";
        definition.Fields = infoCard ? [] : [new() { Property = "WorkingTitle", Value = "[redacted]" }];
        if (infoCard)
            definition.InfoCards =
            [
                new()
                {
                    Name = "Details", Type = InfoCardType.User, User = "Reviewer",
                    Fields = [new() { Property = "WorkingTitle", Value = "[redacted]" }]
                }
            ];

        var result = await service.Evaluate(instance, "Review", Values("{}"), default);

        Assert.Null(result.Answers.Single(answer => answer.QuestionName == "Title").Value);
    }

    [Fact]
    public async Task ADefaultSelectedReferenceEnrichesDependentDefaultsImmediately()
    {
        var repository = new Mock<IWorkflowInstanceRepository>(MockBehavior.Strict);
        var (service, instance, model) = Create(repository);
        var definition = model.WorkflowDefinitions["Project"];
        var candidate = FormPreviewPermissionTests.Instance();
        candidate.Properties["WorkingTitle"] = "Referenced title";
        var preferred = new StringPropertyDefinition
            { Name = "PreferredReference", Type = "String", ParentType = definition };
        var reference = new ReferencePropertyDefinition
        {
            Name = "Reference", Type = "Project", ParentType = definition,
            WorkflowDefinition = definition, Default = "PreferredReference"
        };
        definition.Properties.Add(preferred);
        definition.Properties.Add(reference);
        definition.Fields = [.. definition.Fields, new() { Property = "PreferredReference" }];
        instance.Properties["PreferredReference"] = candidate.Id;
        var title = definition.Properties.Single(property => property.Name == "Title");
        title.Default = "Reference.WorkingTitle";
        definition.Forms.Single(form => form.Name == "Review").Pages[0].PageElements =
        [
            new() { Question = reference.Name, QuestionDefinition = reference },
            new() { Question = title.Name, QuestionDefinition = title }
        ];
        repository.Setup(r => r.GetByWorkflowDefinition("Project", It.IsAny<FilterDefinition<WorkflowInstance>>(),
            It.IsAny<CancellationToken>())).ReturnsAsync([candidate]);

        var preview = await service.Evaluate(instance, "Review", Values("{}"), default);

        Assert.Equal(candidate.Id,
            preview.Answers.Single(answer => answer.QuestionName == "Reference").Value?.GetString());
        Assert.Equal("Referenced title",
            preview.Answers.Single(answer => answer.QuestionName == "Title").Value?.GetString());
    }

    [Fact]
    public async Task TypedCurrencyDefaultsUseReadableProjectValues()
    {
        var (service, instance, model) = Create();
        var definition = model.WorkflowDefinitions["Project"];
        var input = new CurrencyPropertyDefinition
            { Name = "Money", Type = "Currency", Default = "Budget", ParentType = definition };
        definition.Properties.Add(input);
        definition.Properties.Add(new CurrencyPropertyDefinition
            { Name = "Budget", Type = "Currency", ParentType = definition });
        definition.Fields = [new() { Property = "Budget" }];
        definition.Forms.Single(form => form.Name == "Review").Pages[0].PageElements =
            [new() { Question = "Money", QuestionDefinition = input }];
        instance.Properties["Budget"] = new CurrencyAmount("EUR", 12.5).ToBsonDocument();

        var result = await service.Evaluate(instance, "Review", Values("{}"), default);

        Assert.Equal(12.5, result.Answers.Single().Value!.Value.GetProperty("amount").GetDouble());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HiddenQuestionDefaultsRequireTheNormalHiddenFieldGrant(bool granted)
    {
        var (service, instance, model) = Create();
        var definition = model.WorkflowDefinitions["Project"];
        var score = definition.Properties.Single(question => question.Name == "Score");
        score.Visibility = PropertyVisibility.Hidden;
        score.Default = "5";
        if (granted)
            definition.Roles.Single().Actions.Add(new() { Type = RoleAction.ViewHidden, Form = "Review" });

        var result = await service.Evaluate(instance, "Review", Values("{}"), default);

        var answer = result.Answers.Single(answer => answer.QuestionName == "Score");
        Assert.Equal(granted, answer.IsVisible);
        Assert.Equal(granted ? 5d : (double?)null, answer.Value?.GetDouble());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReferenceChoicesAreProvidedAndForgedSelectionsAreRejected(bool array)
    {
        var repository = new Mock<IWorkflowInstanceRepository>(MockBehavior.Strict);
        var (service, instance, model) = Create(repository);
        var definition = model.WorkflowDefinitions["Project"];
        var candidate = FormPreviewPermissionTests.Instance();
        candidate.Properties["WorkingTitle"] = "Reference title";
        definition.InstanceTitle = "{{ WorkingTitle }}";
        var reference = new ReferencePropertyDefinition
        {
            Name = "Reference", Type = array ? "[Project]" : "Project", ParentType = definition,
            WorkflowDefinition = definition
        };
        definition.Properties.Add(reference);
        definition.Forms.Single(f => f.Name == "Review").Pages[0].PageElements =
        [
            new() { Question = "Reference", QuestionDefinition = reference },
            new() { Question = "Title", QuestionDefinition = definition.Properties.Single(q => q.Name == "Title") }
        ];
        var title = definition.Properties.Single(q => q.Name == "Title");
        title.Default = "Reference.WorkingTitle";
        if (array) title.Type = "[String]";
        repository.Setup(r => r.GetByWorkflowDefinition("Project", It.IsAny<FilterDefinition<WorkflowInstance>>(),
            It.IsAny<CancellationToken>())).ReturnsAsync([candidate]);

        var initial = await service.Evaluate(instance, "Review", Values("{}"), default);
        Assert.Equal(candidate.Id, initial.ReferenceChoices["Reference"].Single().Name);
        Assert.Equal("Reference title", initial.ReferenceChoices["Reference"].Single().Text!.En);
        var selection = array ? $"[\"{candidate.Id}\",null]" : $"\"{candidate.Id}\"";
        var accepted = await service.Evaluate(instance, "Review", Values("{\"Reference\":" + selection + "}"), default);
        var selected = accepted.Answers.Single(a => a.QuestionName == "Reference").Value!.Value;
        var defaultTitle = accepted.Answers.Single(a => a.QuestionName == "Title").Value!.Value;
        if (array)
        {
            Assert.Equal([candidate.Id, null], selected.EnumerateArray().Select(item => item.GetString()));
            Assert.Equal(["Reference title", null], defaultTitle.EnumerateArray().Select(item => item.GetString()));
        }
        else
        {
            Assert.Equal(candidate.Id, selected.GetString());
            Assert.Equal("Reference title", defaultTitle.GetString());
        }

        var forged = array ? "[\"forged\"]" : "\"forged\"";
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.Evaluate(instance, "Review", Values("{\"Reference\":" + forged + "}"), default));
    }

    [Theory]
    [InlineData("File", "null")]
    [InlineData("Currency", "{\"currency\":\"EUR\",\"amount\":{}}")]
    [InlineData("User", "{\"userName\":[]}")]
    public async Task SideEffectingOrMalformedInputsAreRejectedAsInvalidInput(string type, string value)
    {
        var (service, instance, model) = Create();
        PropertyDefinition property = type switch
        {
            "File" => new FilePropertyDefinition(),
            "Currency" => new CurrencyPropertyDefinition(),
            _ => new UserPropertyDefinition()
        };
        property.Name = "Input";
        property.Type = type;
        property.ParentType = model.WorkflowDefinitions["Project"];
        property.ParentType.Properties.Add(property);
        property.ParentType.Forms.Single(f => f.Name == "Review").Pages[0].PageElements =
            [new() { Question = "Input", QuestionDefinition = property }];

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.Evaluate(instance, "Review", Values("{\"Input\":" + value + "}"), default));
    }

    [Fact]
    public async Task TargetPreviewKeepsOwnedPagesAndSourceQuestionsWithoutSavedOrParentAnswers()
    {
        var (service, instance, model) = Create();
        var definition = model.WorkflowDefinitions["Project"];
        var form = definition.Forms.Single(f => f.Name == "Review");
        var target = new Form { Name = "Shared", WorkflowDefinition = definition, Pages = [.. form.Pages] };
        target.Pages[0].Sources = ["Assessment"];
        target.Pages.Add(new Page
        {
            Name = "Foreign", Sources = ["Other"], PageElements =
            [
                new() { Question = "Score", QuestionDefinition = definition.Properties.Single(p => p.Name == "Score") }
            ]
        });
        form.PropertyName = "Assessment";
        form.TargetForm = target;
        form.TargetFormName = target.Name;
        definition.Properties.Add(new ObjectPropertyDefinition
            { Name = "Assessment", Type = "Project", WorkflowDefinition = definition, ParentType = definition });
        instance.Properties["Score"] = 10;
        instance.Properties["Assessment"] = new BsonDocument { ["Score"] = 9, ["SourceAnswer"] = "saved" };

        var initial = await service.Evaluate(instance, "Review", Values("{}"), default);
        var changed = await service.Evaluate(instance, "Review", Values("""{"Score":2,"SourceAnswer":"temporary"}"""),
            default);

        Assert.Equal(["Content"], initial.Form.ActualForm.Pages.Select(p => p.Name));
        Assert.Null(initial.Answers.Single(a => a.QuestionName == "Score").Value);
        Assert.True(initial.Answers.Single(a => a.QuestionName == "SourceAnswer").IsVisible);
        Assert.Equal("temporary", changed.Answers.Single(a => a.QuestionName == "SourceAnswer").Value?.GetString());
        Assert.Equal(2m, changed.PageResults.Single().WeightedAverage);
        Assert.Equal(2, target.Pages.Count);
        Assert.Equal(9, instance.Properties["Assessment"]["Score"].AsInt32);
    }

    [Theory]
    [InlineData("{\"WorkingTitle\":\"overwrite project\"}")]
    [InlineData("{\"Events\":{}}")]
    [InlineData("{\"Score\":\"wrong type\"}")]
    [InlineData("{\"Score\":[]}")]
    public async Task PreviewRejectsInputsOutsideTheSelectedFormOrWithWrongTypes(string input)
    {
        var (service, instance, _) = Create();
        await Assert.ThrowsAsync<ArgumentException>(() => service.Evaluate(instance, "Review", Values(input), default));
        Assert.Empty(instance.Properties);
    }

    [Fact]
    public async Task ExplicitNullClearsADefaultAndHiddenQuestionsCannotBeInjected()
    {
        var (service, instance, model) = Create();
        instance.Properties["WorkingTitle"] = "Project";
        var result = await service.Evaluate(instance, "Review", Values("""{"Title":null}"""), default);
        Assert.Null(result.Answers.Single(a => a.QuestionName == "Title").Value);
        model.WorkflowDefinitions["Project"].Properties.Single(q => q.Name == "Score").Visibility =
            PropertyVisibility.Hidden;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.Evaluate(instance, "Review", Values("{\"Score\":2}"), default));
    }

    [Fact]
    public async Task PreviewLoadsOnlyReadableReferencedProjectDetails()
    {
        var course = new WorkflowInstance
        {
            Id = ObjectId.GenerateNewId().ToString(), WorkflowDefinition = "Course", Events = [],
            Properties = new() { ["Name"] = "Permitted course", ["Secret"] = "Not readable" }
        };
        var repository = new Mock<IWorkflowInstanceRepository>(MockBehavior.Strict);
        repository.Setup(r => r.GetById(course.Id, It.IsAny<CancellationToken>())).ReturnsAsync(course);
        var (service, instance, model) = Create(repository);
        var definition = model.WorkflowDefinitions["Project"];
        var courseDefinition = new WorkflowDefinition
        {
            Name = "Course", Roles = definition.Roles, AllSteps = [], Forms = [],
            Properties =
            [
                new StringPropertyDefinition { Name = "Name", Type = "String" },
                new StringPropertyDefinition { Name = "Secret", Type = "String" }
            ]
        };
        model.WorkflowDefinitions.Add("Course", courseDefinition);
        definition.Properties.Add(new ReferencePropertyDefinition
            { Name = "Course", Type = "Course", WorkflowDefinition = courseDefinition, ParentType = definition });
        definition.Fields = [new() { Property = "Course.Name" }];
        definition.Properties.Single(p => p.Name == "Title").Default = "Course.Name";
        definition.Properties.Single(p => p.Name == "SourceAnswer").Default = "Course.Secret";
        instance.Properties["Course"] = course.Id;

        var result = await service.Evaluate(instance, "Review", Values("{}"), default);

        Assert.Equal("Permitted course", result.Answers.Single(a => a.QuestionName == "Title").Value?.GetString());
        Assert.Null(result.Answers.Single(a => a.QuestionName == "SourceAnswer").Value);
    }

    [Theory]
    [InlineData("[String]", "[\"one\",\"two\"]")]
    [InlineData("Date", "\"2026-11-13\"")]
    [InlineData("Currency", "{\"currency\":\"EUR\",\"amount\":12.5}")]
    [InlineData("User", "{\"userName\":\"someone\",\"displayName\":\"Someone\",\"email\":\"someone@example.org\"}")]
    public async Task OrdinaryInputsWorkWithoutPersistingAnything(string type, string value)
    {
        var (service, instance, model) = Create();
        var property = type switch
        {
            "Date" => (PropertyDefinition)new DatePropertyDefinition(),
            "Currency" => new CurrencyPropertyDefinition(),
            "User" => new UserPropertyDefinition(),
            _ => new StringPropertyDefinition()
        };
        property.Name = "Input";
        property.Type = type;
        property.ParentType = model.WorkflowDefinitions["Project"];
        property.ParentType.Properties.Add(property);
        property.ParentType.Forms.Single(f => f.Name == "Review").Pages[0].PageElements =
            [new() { Question = "Input", QuestionDefinition = property }];

        var result = await service.Evaluate(instance, "Review", Values("{\"Input\":" + value + "}"), default);

        Assert.NotNull(result.Answers.Single().Value);
        Assert.Empty(instance.Properties);
    }

    [Fact]
    public async Task PreviewUsesTemporaryAnswersAndReadableProjectDefaultsWithoutChangingSavedData()
    {
        var (service, instance, _) = Create();
        instance.Properties["Score"] = 9;
        instance.Properties["WorkingTitle"] = "Readable project title";
        instance.Properties["SourceAnswer"] = "Private source answer";
        var before = instance.ToJson();

        var initial = await service.Evaluate(instance, "Review", new Dictionary<string, JsonElement?>(), default);
        var changed = await service.Evaluate(instance, "Review", Values("""{"Score":2,"ShowExtra":true}"""), default);

        Assert.Null(initial.Answers.Single(a => a.QuestionName == "Score").Value);
        Assert.Equal("Readable project title",
            initial.Answers.Single(a => a.QuestionName == "Title").Value?.GetString());
        Assert.Null(initial.Answers.Single(a => a.QuestionName == "SourceAnswer").Value);
        Assert.False(initial.Answers.Single(a => a.QuestionName == "Extra").IsVisible);
        Assert.True(changed.Answers.Single(a => a.QuestionName == "Extra").IsVisible);
        Assert.Equal(2, changed.Answers.Single(a => a.QuestionName == "Score").Value?.GetDouble());
        Assert.Equal(2m, changed.PageResults.Single().WeightedAverage);
        Assert.Equal(before, instance.ToJson());
    }

    internal static Dictionary<string, JsonElement?> Values(string json)
        => JsonSerializer.Deserialize<Dictionary<string, JsonElement?>>(json)!;

    internal static (FormPreviewService Service, WorkflowInstance Instance, ModelService Model) Create(
        Mock<IWorkflowInstanceRepository>? repository = null)
    {
        var model = new ModelService(new ModelParser(new DictionaryProvider(new()
        {
            ["Common/Roles/Reviewer.yaml"] = "name: Reviewer\nactions:\n  - type: View\n",
            ["Project/Entity.yaml"] = """
                                      name: Project
                                      fields:
                                        - property: WorkingTitle
                                      properties:
                                        - name: WorkingTitle
                                          type: String
                                        - name: Score
                                          type: Double
                                          calculation:
                                            type: Average
                                            weight: 1
                                        - name: ShowExtra
                                          type: Check
                                        - name: Extra
                                          type: String!
                                          condition:
                                            value:
                                              property: ShowExtra
                                              equal: true
                                        - name: Title
                                          type: String
                                          default: WorkingTitle
                                        - name: SourceAnswer
                                          type: String
                                          sources: [Source]
                                      globalActions:
                                        - type: Preview
                                          roles: [Reviewer]
                                          forms: [Review]
                                      """,
            ["Project/Forms/Review.yaml"] = """
                                            name: Review
                                            pages:
                                              - name: Content
                                                elements:
                                                  - question: Score
                                                  - question: ShowExtra
                                                  - question: Extra
                                                  - question: Title
                                                  - question: SourceAnswer
                                            """,
            ["Project/Forms/Source.yaml"] =
                "name: Source\npages:\n  - name: Other\n    elements:\n      - question: SourceAnswer"
        })));
        var users = new Mock<IUserService>();
        users.Setup(u => u.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync(["Reviewer"]);
        repository ??= new Mock<IWorkflowInstanceRepository>(MockBehavior.Strict);
        var rights = new RightsService(model, users.Object, repository.Object,
            UvA.Workflow.Tests.Helpers.UnitTestsHelpers.CreateStateResolver(model, repository.Object));
        return (new FormPreviewService(model, rights, repository.Object), FormPreviewPermissionTests.Instance(), model);
    }
}