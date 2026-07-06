using Microsoft.Extensions.Configuration;
using Moq;
using UvA.Workflow.Notifications;
using UvA.Workflow.Tests.Helpers;

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
        string? frontendBaseUrl = null, string hostEnvironment = "Production")
    {
        var layout = new CapturingLayout();
        var resolver = new Mock<IMailLayoutResolver>();
        resolver.Setup(r => r.Resolve(It.IsAny<string?>())).Returns(layout);

        var config = new Mock<IConfiguration>();
        config.Setup(c => c["FrontendBaseUrl"]).Returns(frontendBaseUrl);

        return (UnitTestsHelpers.CreateMailBuilder(resolver.Object, config.Object, environmentName: hostEnvironment),
            layout, resolver);
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
            To = "student@uva.nl",
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
            To = "student@uva.nl",
            Subject = new BilingualString("Subject", ""),
            Body = new BilingualString("**bold text**", "")
        };

        await builder.BuildAsync(instance, sendMail, _modelService);

        Assert.NotNull(layout.CapturedBody);
        Assert.Contains("<strong>bold text</strong>", layout.CapturedBody);
    }

    [Fact]
    public async Task BuildAsync_WithToLiteralAddress_ResolvesRecipient()
    {
        var (builder, _, _) = CreateBuilder();
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();
        var sendMail = new SendMessage
        {
            To = "fixed@uva.nl",
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
    public async Task BuildAsync_WithPreferredLanguage_UsesRecipientLanguage()
    {
        var (builder, layout, _) = CreateBuilder();
        var userDoc = new MongoDB.Bson.BsonDocument
        {
            { "_id", MongoDB.Bson.ObjectId.GenerateNewId() },
            { "UserName", "jdoe" },
            { "DisplayName", "Jane Doe" },
            { "Email", "j.doe@uva.nl" },
            { "PreferredLanguage", "nl-NL" }
        };
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithProperties(
                ("Supervisor", _ => userDoc),
                ("Title", b => b.Value("Mijn scriptie")))
            .Build();
        var sendMail = new SendMessage
        {
            To = "Supervisor",
            Subject = new BilingualString("Thesis: {{ Title }}", "Scriptie: {{ Title }}"),
            Body = new BilingualString("English body", "Nederlandse **tekst**"),
            Buttons =
            [
                new SendMessageButton
                {
                    Url = "https://example.com",
                    Label = new BilingualString("Open", "Openen")
                }
            ]
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        Assert.Equal("Scriptie: Mijn scriptie", result.Subject);
        Assert.NotNull(layout.CapturedBody);
        Assert.Contains("Nederlandse <strong>tekst</strong>", layout.CapturedBody);
        var button = Assert.Single(layout.CapturedButtons!);
        Assert.Equal("Openen", button.Label);
    }

    [Fact]
    public async Task BuildAsync_WithPreferredLanguage_FallsBackToEnglishWhenTranslationIsEmpty()
    {
        var (builder, _, _) = CreateBuilder();
        var userDoc = new MongoDB.Bson.BsonDocument
        {
            { "_id", MongoDB.Bson.ObjectId.GenerateNewId() },
            { "UserName", "jdoe" },
            { "DisplayName", "Jane Doe" },
            { "Email", "j.doe@uva.nl" },
            { "PreferredLanguage", "nl" }
        };
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithProperties(("Supervisor", _ => userDoc))
            .Build();
        var sendMail = new SendMessage
        {
            To = "Supervisor",
            Subject = new BilingualString("English subject", ""),
            Body = new BilingualString("English body", "")
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        Assert.Equal("English subject", result.Subject);
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
            To = "student@uva.nl",
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
            To = "student@uva.nl",
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
            To = "student@uva.nl",
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
        var supervisorDoc = new MongoDB.Bson.BsonDocument
        {
            { "_id", MongoDB.Bson.ObjectId.GenerateNewId() },
            { "UserName", "supervisor" },
            { "DisplayName", "Test Supervisor" },
            { "Email", "test_address@invalid.invalid" }
        };

        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithProperties(("Supervisor", _ => supervisorDoc))
            .Build();

        var sendMail = new SendMessage
        {
            TemplateKey = "SubjectSubmitted",
            To = "Supervisor"
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        // Subject / recipient come from template defaults
        Assert.Equal("You have submitted your thesis proposal", result.Subject);
        var recipient = Assert.Single(result.To);
        Assert.Equal("test_address@invalid.invalid", recipient.MailAddress);

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
            To = "override@uva.nl",
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
            TemplateKey = "FinalVersionSubmitted",
            To = "test_address@invalid.invalid"
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        // FinalVersionSubmitted template uses:
        // {{ coalesce(Student.DisplayName, =student) }}
        // so without Student data it should fall back to "student".
        Assert.Contains("Congratulations student", result.Body);
    }

    [Fact]
    public async Task BuildAsync_WithTemplateKeyAndToUserProperty_UsesUserRecipient()
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

    [Fact]
    public async Task BuildAsync_AppendsWorkerGroupToSubject_WhenNotProd()
    {
        var (builder, _, _) = CreateBuilder(hostEnvironment: "Development");
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();
        var sendMail = new SendMessage
        {
            To = "student@uva.nl",
            Subject = new BilingualString("My Subject", ""),
            Body = new BilingualString("", "")
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        Assert.Equal("[test] My Subject", result.Subject);
    }

    private static MongoDB.Bson.BsonDocument UserDoc(string name, string email, string? language = null)
    {
        var doc = new MongoDB.Bson.BsonDocument
        {
            { "_id", MongoDB.Bson.ObjectId.GenerateNewId() },
            { "UserName", name },
            { "DisplayName", name },
            { "Email", email }
        };
        if (language != null)
            doc["PreferredLanguage"] = language;
        return doc;
    }

    [Fact]
    public async Task BuildAsync_WhenAllToUsersPreferDutch_SendsDutch()
    {
        var (builder, _, _) = CreateBuilder();
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Context", currentStep: "Start")
            .WithProperties(("Coordinator", _ => new MongoDB.Bson.BsonArray
            {
                UserDoc("Alice", "alice@uva.nl", "nl-NL"),
                UserDoc("Bob", "bob@uva.nl", "nl")
            }))
            .Build();
        var sendMail = new SendMessage
        {
            To = "Coordinator",
            Subject = new BilingualString("English subject", "Nederlands onderwerp"),
            Body = new BilingualString("", "")
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        Assert.Equal("Nederlands onderwerp", result.Subject);
    }

    [Fact]
    public async Task BuildAsync_WhenAnyToUserDoesNotPreferDutch_SendsEnglish()
    {
        var (builder, _, _) = CreateBuilder();
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Context", currentStep: "Start")
            .WithProperties(("Coordinator", _ => new MongoDB.Bson.BsonArray
            {
                UserDoc("Alice", "alice@uva.nl", "nl"),
                UserDoc("Bob", "bob@uva.nl", "en")
            }))
            .Build();
        var sendMail = new SendMessage
        {
            To = "Coordinator",
            Subject = new BilingualString("English subject", "Nederlands onderwerp"),
            Body = new BilingualString("", "")
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        Assert.Equal("English subject", result.Subject);
    }

    [Fact]
    public async Task BuildAsync_WithToPointingToUserArray_MailsEveryUser()
    {
        var (builder, _, _) = CreateBuilder();
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Context", currentStep: "Start")
            .WithProperties(("Coordinator", _ => new MongoDB.Bson.BsonArray
            {
                UserDoc("Alice", "alice@uva.nl"),
                UserDoc("Bob", "bob@uva.nl")
            }))
            .Build();
        var sendMail = new SendMessage
        {
            To = "Coordinator",
            Subject = new BilingualString("Subject", ""),
            Body = new BilingualString("", "")
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        Assert.Equal(["alice@uva.nl", "bob@uva.nl"], result.To.Select(r => r.MailAddress));
    }

    [Fact]
    public async Task BuildAsync_WithToListCcAndBcc_ResolvesUsersAndAddresses()
    {
        var (builder, _, _) = CreateBuilder();
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Context", currentStep: "Start")
            .WithProperties(
                ("Coordinator", _ => new MongoDB.Bson.BsonArray
                {
                    UserDoc("Alice", "alice@uva.nl"),
                    UserDoc("Bob", "bob@uva.nl")
                }),
                ("Impersonator", _ => new MongoDB.Bson.BsonArray { UserDoc("Carol", "carol@uva.nl") }))
            .Build();
        var sendMail = new SendMessage
        {
            To = new Recipients { "Coordinator", "extra@uva.nl" },
            Cc = "Impersonator",
            Bcc = "archive@uva.nl",
            Subject = new BilingualString("Subject", ""),
            Body = new BilingualString("", "")
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        Assert.Equal(["alice@uva.nl", "bob@uva.nl", "extra@uva.nl"], result.To.Select(r => r.MailAddress));
        Assert.NotNull(result.Cc);
        Assert.Equal(["carol@uva.nl"], result.Cc!.Select(r => r.MailAddress));
        Assert.NotNull(result.Bcc);
        Assert.Equal(["archive@uva.nl"], result.Bcc!.Select(r => r.MailAddress));
    }

    [Fact]
    public async Task BuildAsync_WithoutCcOrBcc_LeavesThemNull()
    {
        var (builder, _, _) = CreateBuilder();
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();
        var sendMail = new SendMessage
        {
            To = "student@uva.nl",
            Subject = new BilingualString("Subject", ""),
            Body = new BilingualString("", "")
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        Assert.Null(result.Cc);
        Assert.Null(result.Bcc);
    }

    [Fact]
    public async Task BuildAsync_WithDuplicateRecipients_DeduplicatesByEmail()
    {
        var (builder, _, _) = CreateBuilder();
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Context", currentStep: "Start")
            .WithProperties(("Coordinator", _ => new MongoDB.Bson.BsonArray
            {
                UserDoc("Alice", "alice@uva.nl"),
                UserDoc("Bob", "bob@uva.nl")
            }))
            .Build();
        var sendMail = new SendMessage
        {
            // Alice appears both as a resolved user and as a literal address
            To = new Recipients { "Coordinator", "ALICE@uva.nl" },
            Subject = new BilingualString("Subject", ""),
            Body = new BilingualString("", "")
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        Assert.Equal(["alice@uva.nl", "bob@uva.nl"], result.To.Select(r => r.MailAddress));
    }

    [Fact]
    public async Task BuildAsync_WithBccOnly_DoesNotAddInvalidToRecipient()
    {
        var (builder, _, _) = CreateBuilder();
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();
        var sendMail = new SendMessage
        {
            Bcc = "archive@uva.nl",
            Subject = new BilingualString("Subject", ""),
            Body = new BilingualString("", "")
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        Assert.Empty(result.To);
        Assert.NotNull(result.Bcc);
        Assert.Equal(["archive@uva.nl"], result.Bcc!.Select(r => r.MailAddress));
    }

    [Fact]
    public async Task BuildAsync_WithNoRecipients_FallsBackToInvalidRecipient()
    {
        var (builder, _, _) = CreateBuilder();
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();
        var sendMail = new SendMessage
        {
            Subject = new BilingualString("Subject", ""),
            Body = new BilingualString("", "")
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        var recipient = Assert.Single(result.To);
        Assert.Equal("invalid@invalid", recipient.MailAddress);
    }

    [Fact]
    public async Task BuildAsync_WithTemplatedToAddress_EvaluatesTemplateAsAddress()
    {
        var (builder, _, _) = CreateBuilder();
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithProperties(("Title", b => b.Value("contact@uva.nl")))
            .Build();
        var sendMail = new SendMessage
        {
            To = "{{ Title }}",
            Subject = new BilingualString("Subject", ""),
            Body = new BilingualString("", "")
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        var recipient = Assert.Single(result.To);
        Assert.Equal("contact@uva.nl", recipient.MailAddress);
    }

    [Fact]
    public async Task BuildAsync_WithTemplatedToAddressThatResolvesBlank_DropsIt()
    {
        var (builder, _, _) = CreateBuilder();
        // Title is not set, so "{{ Title }}" evaluates to an empty string and must be dropped
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();
        var sendMail = new SendMessage
        {
            To = "{{ Title }}",
            Subject = new BilingualString("Subject", ""),
            Body = new BilingualString("", "")
        };

        var result = await builder.BuildAsync(instance, sendMail, _modelService);

        // No real recipient survived → the misconfigured-mail fallback kicks in
        var recipient = Assert.Single(result.To);
        Assert.Equal("invalid@invalid", recipient.MailAddress);
    }
}