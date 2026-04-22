using Microsoft.Extensions.Configuration;
using Moq;
using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Notifications;
using UvA.Workflow.Persistence;

namespace UvA.Workflow.Tests;

public class MailBuilderTests
{
    private readonly ModelService _modelService =
        new(new ModelParser(new FileSystemProvider("../../../../Examples/Projects")));

    // Captures whatever htmlBody is passed into Render so tests can assert on it
    private class CapturingLayout : IMailLayout
    {
        public string? CapturedBody { get; private set; }
        public IReadOnlyList<MailButton>? CapturedButtons { get; private set; }

        public string Render(string htmlBody, IReadOnlyList<MailButton> buttons)
        {
            CapturedBody = htmlBody;
            CapturedButtons = buttons;
            return htmlBody;
        }
    }

    private (MailBuilder builder, CapturingLayout layout, Mock<IMailLayoutResolver> resolver) CreateBuilder(
        string? frontendBaseUrl = null)
    {
        var layout = new CapturingLayout();
        var resolver = new Mock<IMailLayoutResolver>();
        resolver.Setup(r => r.Resolve(It.IsAny<string?>())).Returns(layout);

        var config = new Mock<IConfiguration>();
        config.Setup(c => c["FrontendBaseUrl"]).Returns(frontendBaseUrl);

        return (new MailBuilder(resolver.Object, config.Object), layout, resolver);
    }

    [Fact]
    public async Task BuildAsync_ResolvesSubjectTemplate()
    {
        var (builder, _, _) = CreateBuilder();
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithProperties(("Title", b => b.Value("My Thesis")))
            .Build();
        var sendMail = new SendMessage
        {
            ToAddress = "student@uva.nl",
            Subject = new BilingualString("Thesis: {{ Title }}", ""),
            Body = new BilingualString("", "")
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        Assert.Equal("Thesis: My Thesis", result.Subject);
    }

    [Fact]
    public async Task BuildAsync_ConvertsBodyMarkdownToHtml()
    {
        var (builder, layout, _) = CreateBuilder();
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();
        var sendMail = new SendMessage
        {
            ToAddress = "student@uva.nl",
            Subject = new BilingualString("Subject", ""),
            Body = new BilingualString("**bold text**", "")
        };

        await builder.BuildAsync(instance, sendMail, _modelService);

        Assert.NotNull(layout.CapturedBody);
        Assert.Contains("<strong>bold text</strong>", layout.CapturedBody);
    }

    [Fact]
    public async Task BuildAsync_WithToAddressTemplate_ResolvesRecipientFromTemplate()
    {
        var (builder, _, _) = CreateBuilder();
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();
        var sendMail = new SendMessage
        {
            ToAddress = "fixed@uva.nl",
            Subject = new BilingualString("Subject", ""),
            Body = new BilingualString("", "")
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        Assert.Single(result.To);
        Assert.Equal("fixed@uva.nl", result.To[0].MailAddress);
    }

    [Fact]
    public async Task BuildAsync_WithToPropertyPointingToUser_ResolvesRecipientFromUser()
    {
        var (builder, _, _) = CreateBuilder();
        var userDoc = new MongoDB.Bson.BsonDocument
        {
            { "_id", MongoDB.Bson.ObjectId.GenerateNewId() },
            { "UserName", "jdoe" },
            { "DisplayName", "Jane Doe" },
            { "Email", "j.doe@uva.nl" }
        };
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithProperties(("Supervisor", _ => userDoc))
            .Build();
        var sendMail = new SendMessage
        {
            To = "Supervisor",
            Subject = new BilingualString("Subject", ""),
            Body = new BilingualString("", "")
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        Assert.Single(result.To);
        Assert.Equal("j.doe@uva.nl", result.To[0].MailAddress);
        Assert.Equal("Jane Doe", result.To[0].DisplayName);
    }

    [Fact]
    public async Task BuildAsync_PassesButtonsWithResolvedUrlAndLabel()
    {
        var (builder, layout, _) = CreateBuilder(frontendBaseUrl: "https://milestones.uva.nl");
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();
        var sendMail = new SendMessage
        {
            ToAddress = "student@uva.nl",
            Subject = new BilingualString("Subject", ""),
            Body = new BilingualString("", ""),
            Buttons =
            [
                new SendMessageButton
                {
                    Url = "{{ FrontendBaseUrl }}/project",
                    Label = new BilingualString("Open", "Open")
                }
            ]
        };

        await builder.BuildAsync(instance, sendMail, _modelService);

        Assert.NotNull(layout.CapturedButtons);
        var button = Assert.Single(layout.CapturedButtons);
        Assert.Equal("https://milestones.uva.nl/project", button.Url);
        Assert.Equal("Open", button.Label);
    }

    [Fact]
    public async Task BuildAsync_ForwardsLayoutKeyToResolver()
    {
        var (builder, _, resolver) = CreateBuilder();
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();
        var sendMail = new SendMessage
        {
            ToAddress = "student@uva.nl",
            Subject = new BilingualString("Subject", ""),
            Body = new BilingualString("", ""),
            Layout = "custom-layout"
        };

        await builder.BuildAsync(instance, sendMail, _modelService);

        resolver.Verify(r => r.Resolve("custom-layout"), Times.Once);
    }

    [Fact]
    public async Task BuildAsync_InjectsFrontendBaseUrlIntoContext()
    {
        var (builder, _, _) = CreateBuilder(frontendBaseUrl: "https://milestones.uva.nl/");
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();
        var sendMail = new SendMessage
        {
            ToAddress = "student@uva.nl",
            Subject = new BilingualString("Link: {{ FrontendBaseUrl }}", ""),
            Body = new BilingualString("", "")
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        // Trailing slash should be trimmed
        Assert.Equal("Link: https://milestones.uva.nl", result.Subject);
    }

    [Fact]
    public async Task BuildAsync_WithTemplateKey_LoadsTemplateAndUsesDefaults()
    {
        var (builder, layout, resolver) = CreateBuilder(frontendBaseUrl: "https://milestones.uva.nl");
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();

        var sendMail = new SendMessage
        {
            TemplateKey = "SubjectSubmitted"
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        // Subject / recipient come from template defaults
        Assert.Equal("You have submitted your thesis proposal", result.Subject);
        var recipient = Assert.Single(result.To);
        Assert.Equal("replaced-by-override@mail.com", recipient.MailAddress);

        // Layout key comes from template defaults
        resolver.Verify(r => r.Resolve("default"), Times.Once);

        // Button comes from template and has resolved FrontendBaseUrl + instance id
        Assert.NotNull(layout.CapturedButtons);
        var button = Assert.Single(layout.CapturedButtons);
        Assert.Equal("View your submission", button.Label);
        Assert.StartsWith("https://milestones.uva.nl/instance/", button.Url);
    }

    [Fact]
    public async Task BuildAsync_WithTemplateKey_InlineValuesOverrideTemplateDefaults()
    {
        var (builder, layout, resolver) = CreateBuilder(frontendBaseUrl: "https://milestones.uva.nl");
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithProperties(("Title", b => b.Value("Custom Title")))
            .Build();

        var sendMail = new SendMessage
        {
            TemplateKey = "SubjectSubmitted",
            ToAddress = "override@uva.nl",
            Subject = new BilingualString("Overridden: {{ Title }}", ""),
            Body = new BilingualString("**custom body**", ""),
            Layout = "custom-layout",
            Buttons =
            [
                new SendMessageButton
                {
                    Label = new BilingualString("Custom action", "Custom action"),
                    Url = "{{ FrontendBaseUrl }}/custom"
                }
            ]
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        Assert.Equal("Overridden: Custom Title", result.Subject);
        var recipient = Assert.Single(result.To);
        Assert.Equal("override@uva.nl", recipient.MailAddress);

        resolver.Verify(r => r.Resolve("custom-layout"), Times.Once);

        Assert.NotNull(layout.CapturedBody);
        Assert.Contains("<strong>custom body</strong>", layout.CapturedBody);

        Assert.NotNull(layout.CapturedButtons);
        var button = Assert.Single(layout.CapturedButtons);
        Assert.Equal("Custom action", button.Label);
        Assert.Equal("https://milestones.uva.nl/custom", button.Url);
    }

    [Fact]
    public async Task BuildAsync_WithUnknownTemplateKey_ThrowsWithKnownTemplates()
    {
        var (builder, _, _) = CreateBuilder();
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();

        var sendMail = new SendMessage
        {
            TemplateKey = "DoesNotExist"
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            builder.BuildAsync(instance, sendMail, _modelService));

        Assert.Contains("Mail template 'DoesNotExist' not found in 'Project'", ex.Message);
        Assert.Contains("Known templates:", ex.Message);
        Assert.Contains("SubjectSubmitted", ex.Message);
    }

    [Fact]
    public async Task BuildAsync_WithTemplateDefaultExpression_UsesFallbackWhenDataMissing()
    {
        var (builder, _, _) = CreateBuilder();
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();

        var sendMail = new SendMessage
        {
            TemplateKey = "FinalVersionSubmitted"
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        // FinalVersionSubmitted template uses:
        // {{ coalesce(Student.DisplayName, =student) }}
        // so without Student data it should fall back to "student".
        Assert.Contains("Congratulations student", result.Body);
    }

    [Fact]
    public async Task BuildAsync_WithTemplateKeyAndToUserProperty_UsesUserRecipientInsteadOfTemplateToAddress()
    {
        var (builder, _, _) = CreateBuilder();

        var reviewerDoc = new MongoDB.Bson.BsonDocument
        {
            { "_id", MongoDB.Bson.ObjectId.GenerateNewId() },
            { "UserName", "rjansen" },
            { "DisplayName", "Robin Jansen" },
            { "Email", "robin.jansen@uva.nl" }
        };

        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project-PA", currentStep: "AssessmentExaminer")
            .WithProperties(("SecondReviewer", _ => reviewerDoc))
            .Build();

        // Mirrors AssessmentExaminer.yaml:
        // - sendMail:
        //     template: AssessmentRequestAssessor
        //     to: SecondReviewer
        var sendMail = new SendMessage
        {
            TemplateKey = "AssessmentRequestAssessor",
            To = "SecondReviewer"
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        var recipient = Assert.Single(result.To);
        Assert.Equal("robin.jansen@uva.nl", recipient.MailAddress);
        Assert.Equal("Robin Jansen", recipient.DisplayName);
    }
}