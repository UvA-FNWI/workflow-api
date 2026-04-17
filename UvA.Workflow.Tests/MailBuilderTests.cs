using Microsoft.Extensions.Configuration;
using Moq;
using UvA.Workflow.Notifications;
using UvA.Workflow.Persistence;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.WorkflowModel;

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
}