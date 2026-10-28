using System.Text.Json;
using System.Text.Json.Serialization;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;

namespace UvA.Workflow.Tests;

public class FormDtoTests
{
    [Fact]
    public void Create_CalculatesQuestionPercentagesAcrossAllPages()
    {
        var definition = new WorkflowDefinition { Name = "Assessment" };
        var quality = Question("Quality", definition, 2);
        var depth = Question("Depth", definition, 1);
        var comments = Question("Comments", definition);

        var form = new Form
        {
            Name = "Review",
            WorkflowDefinition = definition,
            Pages =
            [
                new Page
                {
                    Name = "Report",
                    PageElements =
                    [
                        new PageElement { Question = quality.Name, QuestionDefinition = quality },
                        new PageElement { Question = comments.Name, QuestionDefinition = comments },
                    ]
                },
                new Page
                {
                    Name = "Process",
                    PageElements = [new PageElement { Question = depth.Name, QuestionDefinition = depth }]
                }
            ]
        };

        var result = FormDto.Create(form, new ObjectContext([]));

        Assert.Equal(2m / 3m * 100m,
            Assert.IsType<DoubleQuestionDto>(result.Pages[0].Elements.Single(q => q.Question!.Name == "Quality")
                .Question).Percentage);
        Assert.Equal(1m / 3m * 100m,
            Assert.IsType<DoubleQuestionDto>(result.Pages[1].Elements.Single(q => q.Question!.Name == "Depth").Question)
                .Percentage);
        Assert.Null(Assert
            .IsType<DoubleQuestionDto>(result.Pages[0].Elements.Single(q => q.Question!.Name == "Comments").Question)
            .Percentage);
    }

    [Fact]
    public void Create_IncludesConfiguredFileTypesAndDefaultsToPdf()
    {
        var definition = new WorkflowDefinition { Name = "Documents" };
        var configured = new FilePropertyDefinition
        {
            Name = "Archive",
            Type = "File",
            ParentType = definition,
            AllowedTypes = ["zip", "tar.gz"],
            MaximumSize = 25_000_000
        };
        var defaulted = new FilePropertyDefinition
        {
            Name = "Report",
            Type = "File",
            ParentType = definition
        };

        var configuredDto = Assert.IsType<FileQuestionDto>(QuestionDto.Create(configured, 0, definition));
        var defaultedDto = Assert.IsType<FileQuestionDto>(QuestionDto.Create(defaulted, 0, definition));

        Assert.Equal(["zip", "tar.gz"], configuredDto.AllowedFileTypes);
        Assert.Equal(["pdf"], defaultedDto.AllowedFileTypes);
        Assert.Equal(25_000_000, configuredDto.AllowedFileSize);
        Assert.Equal(10_000_000, defaultedDto.AllowedFileSize);
    }

    [Fact]
    public void Create_SetsAllowMessagesFromEntitySettings()
    {
        var definition = new WorkflowDefinition
        {
            Name = "Application",
            Messages = new MessagesSettings { Enabled = true }
        };
        var name = new StringPropertyDefinition { Name = "Name", Type = "String", ParentType = definition };
        var hidden = new StringPropertyDefinition
        {
            Name = "Internal",
            Type = "String",
            ParentType = definition,
            Visibility = PropertyVisibility.Hidden
        };
        var excluded = new StringPropertyDefinition
        {
            Name = "StatusBadge",
            Type = "String",
            ParentType = definition,
            Messages = false
        };
        definition.Properties = [name, hidden, excluded];

        Assert.True(QuestionDto.Create(name, 0, definition).AllowMessages);
        Assert.False(QuestionDto.Create(hidden, 0, definition).AllowMessages);
        Assert.False(QuestionDto.Create(excluded, 0, definition).AllowMessages);
    }

    [Fact]
    public void Create_AllowMessages_UsesFormWorkflowDefinition_WhenPropertyParentTypeIsStale()
    {
        var parent = new WorkflowDefinition { Name = "Thesis-Base" };
        var title = new StringPropertyDefinition { Name = "Title", Type = "String", ParentType = parent };
        parent.Properties = [title];

        var child = new WorkflowDefinition
        {
            Name = "Project-Playground",
            Messages = new MessagesSettings { Enabled = true }
        };
        child.Properties = [title];
        title.ParentType = parent;

        var subject = new StringPropertyDefinition { Name = "Subject", Type = "String", ParentType = child };
        child.Properties.Add(subject);

        var form = new Form
        {
            Name = "Start",
            WorkflowDefinition = child,
            Pages =
            [
                new Page
                {
                    PageElements =
                    [
                        new PageElement { Question = "Title", QuestionDefinition = title },
                        new PageElement { Question = "Subject", QuestionDefinition = subject },
                    ]
                }
            ]
        };

        var questions = FormDto.Create(form, new ObjectContext([])).Pages[0].Elements
            .Select(element => element.Question!)
            .ToDictionary(question => question.Name);

        Assert.True(questions["Title"].AllowMessages);
        Assert.True(questions["Subject"].AllowMessages);
    }

    [Fact]
    public void TextSettings_SerializeDirectlyOnQuestion()
    {
        var parent = new WorkflowDefinition { Name = "Project" };
        var property = new StringPropertyDefinition
        {
            Name = "Notes",
            Type = "String",
            ParentType = parent,
            Control = StringControl.TextArea
        };
        var question = QuestionDto.Create(property, 0, parent);
        var json = JsonSerializer.SerializeToElement(question, CreateJsonOptions());

        Assert.False(json.TryGetProperty("multiline", out _));
        Assert.False(json.TryGetProperty("allowAttachments", out _));
        Assert.Equal("TextArea", json.GetProperty("control").GetString());
        Assert.False(json.TryGetProperty("layout", out _));
        Assert.False(json.TryGetProperty("variant", out _));
    }

    [Fact]
    public void FormSerialization_IncludesOnlyTheSettingsForEachQuestionType()
    {
        var definition = new WorkflowDefinition { Name = "Project" };
        var childDefinition = new WorkflowDefinition { Name = "Assessment", IsEmbedded = true };
        childDefinition.Properties.Add(new StringPropertyDefinition
        {
            Name = "Comments", Type = "String", ParentType = childDefinition
        });
        PropertyDefinition[] properties =
        [
            new DatePropertyDefinition { Name = "Due", Type = "Date" },
            new DateTimePropertyDefinition { Name = "Appointment", Type = "DateTime" },
            new IntPropertyDefinition { Name = "Count", Type = "Int" },
            new DoublePropertyDefinition { Name = "Score", Type = "Double" },
            new CheckPropertyDefinition { Name = "Confirmed", Type = "Check" },
            new CurrencyPropertyDefinition { Name = "Amount", Type = "Currency" },
            new FilePropertyDefinition { Name = "Report", Type = "File" },
            new StringPropertyDefinition { Name = "Title", Type = "String" },
            new UserPropertyDefinition { Name = "Supervisor", Type = "User", AllowsExternalUsers = true },
            new ChoicePropertyDefinition
            {
                Name = "Grade", Type = "Grade", Values = [new Choice { Name = "Pass" }],
                Control = ChoiceControl.RadioList
            },
            new ReferencePropertyDefinition
            {
                Name = "Course", Type = "Context", WorkflowDefinition = new WorkflowDefinition { Name = "Context" },
                Control = ChoiceControl.ComboBox
            },
            new ObjectPropertyDefinition
            {
                Name = "Assessment", Type = "Assessment", WorkflowDefinition = childDefinition,
                Control = ObjectControl.Modal
            }
        ];
        foreach (var property in properties)
            property.ParentType = definition;
        var form = new Form
        {
            Name = "Edit",
            WorkflowDefinition = definition,
            Pages =
            [
                new Page
                {
                    PageElements = properties.Select(property => new PageElement
                    {
                        Question = property.Name, QuestionDefinition = property
                    }).ToArray()
                }
            ]
        };
        var json = JsonSerializer.SerializeToElement(FormDto.Create(form, new ObjectContext([])), CreateJsonOptions());
        var questions = json.GetProperty("pages")[0].GetProperty("elements").EnumerateArray()
            .Select(element => element.GetProperty("question"))
            .ToDictionary(question => question.GetProperty("type").GetString()!);

        var settings = new Dictionary<string, string[]>
        {
            ["Date"] = [],
            ["DateTime"] = [],
            ["Int"] = ["weight", "percentage"],
            ["Double"] = ["weight", "percentage"],
            ["Check"] = [],
            ["Currency"] = ["weight", "percentage"],
            ["File"] = ["allowedFileTypes", "allowedFileSize"],
            ["String"] = ["control", "minLength", "maxLength"],
            ["User"] = ["allowsExternalUsers"],
            ["Choice"] = ["weight", "percentage", "control", "choices", "rubric", "sorting"],
            ["Reference"] = ["control", "workflowDefinition"],
            ["Object"] = ["control", "workflowDefinition", "subProperties"]
        };
        Assert.Equal(properties.Length, questions.Count);
        foreach (var (type, question) in questions)
        {
            Assert.False(question.TryGetProperty("$type", out _));
            foreach (var setting in settings.Values.SelectMany(fields => fields).Distinct())
                Assert.Equal(settings[type].Contains(setting), question.TryGetProperty(setting, out _));
        }

        Assert.Equal("pdf", questions["File"].GetProperty("allowedFileTypes")[0].GetString());
        Assert.True(questions["User"].GetProperty("allowsExternalUsers").GetBoolean());
        Assert.Equal("Pass", questions["Choice"].GetProperty("choices")[0].GetProperty("name").GetString());
        Assert.Equal("RadioList", questions["Choice"].GetProperty("control").GetString());
        Assert.Equal("ComboBox", questions["Reference"].GetProperty("control").GetString());
        Assert.Equal("Modal", questions["Object"].GetProperty("control").GetString());
        var child = questions["Object"].GetProperty("subProperties")[0];
        Assert.Equal("String", child.GetProperty("type").GetString());
        Assert.True(child.TryGetProperty("maxLength", out _));
        Assert.False(child.TryGetProperty("allowAttachments", out _));
        Assert.Equal(JsonValueKind.Null, child.GetProperty("control").ValueKind);
        Assert.False(child.TryGetProperty("allowedFileTypes", out _));
        Assert.False(child.TryGetProperty("weight", out _));
        Assert.False(child.TryGetProperty("percentage", out _));
    }

    private static JsonSerializerOptions CreateJsonOptions() => new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new QuestionJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() }
    };

    private static DoublePropertyDefinition Question(string name, WorkflowDefinition parent, decimal? weight = null) =>
        new()
        {
            Name = name,
            Type = "Double",
            ParentType = parent,
            Calculation = weight == null ? null : new CalculationSettings { Weight = weight }
        };
}