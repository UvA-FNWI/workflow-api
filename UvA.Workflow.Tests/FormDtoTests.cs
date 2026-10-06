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
            FileSettings = new()
            {
                AllowedTypes = ["zip", "tar.gz"],
                MaximumSize = 25_000_000
            }
        };
        var defaulted = new FilePropertyDefinition
        {
            Name = "Report",
            Type = "File",
            ParentType = definition
        };

        var configuredDto = Assert.IsType<FileQuestionDto>(QuestionDto.Create(configured, 0));
        var defaultedDto = Assert.IsType<FileQuestionDto>(QuestionDto.Create(defaulted, 0));

        Assert.Equal(["zip", "tar.gz"], configuredDto.AllowedFileTypes);
        Assert.Equal(["pdf"], defaultedDto.AllowedFileTypes);
        Assert.Equal(25_000_000, configuredDto.AllowedFileSize);
        Assert.Equal(10_000_000, defaultedDto.AllowedFileSize);
    }

    [Fact]
    public void TypedLayout_SerializesExistingQuestionFields()
    {
        var parent = new WorkflowDefinition { Name = "Project" };
        var property = new StringPropertyDefinition
        {
            Name = "Notes",
            Type = "String",
            ParentType = parent,
            Layout = new TextLayoutOptions { Multiline = true, Variant = StringVariant.Email }
        };
        var question = QuestionDto.Create(property, 0);
        var json = JsonSerializer.SerializeToElement(question, CreateJsonOptions());

        var layout = json.GetProperty("layout");
        Assert.True(layout.GetProperty("multiline").GetBoolean());
        Assert.Equal("Email", layout.GetProperty("variant").GetString());
        Assert.False(layout.TryGetProperty("$type", out _));
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
                Layout = new ChoiceLayoutOptions { Type = ChoiceLayoutType.RadioList }
            },
            new ReferencePropertyDefinition
            {
                Name = "Course", Type = "Context", WorkflowDefinition = new WorkflowDefinition { Name = "Context" },
                Layout = new ChoiceLayoutOptions { Type = ChoiceLayoutType.ComboBox }
            },
            new ObjectPropertyDefinition
            {
                Name = "Assessment", Type = "Assessment", WorkflowDefinition = childDefinition,
                Layout = new TableLayoutOptions { Type = TableLayout.Modal }
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
            ["String"] = ["layout", "minLength", "maxLength"],
            ["User"] = ["allowsExternalUsers"],
            ["Choice"] = ["weight", "percentage", "layout", "choices", "rubric", "sorting"],
            ["Reference"] = ["layout", "workflowDefinition"],
            ["Object"] = ["layout", "workflowDefinition", "subProperties"]
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
        Assert.Equal("RadioList", questions["Choice"].GetProperty("layout").GetProperty("type").GetString());
        Assert.Equal("ComboBox", questions["Reference"].GetProperty("layout").GetProperty("type").GetString());
        Assert.Equal("Modal", questions["Object"].GetProperty("layout").GetProperty("type").GetString());
        var child = questions["Object"].GetProperty("subProperties")[0];
        Assert.Equal("String", child.GetProperty("type").GetString());
        Assert.True(child.TryGetProperty("maxLength", out _));
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