using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class EmailTemplateTests
{
    private const string BaseEmail = """
                                     subject: Base subject
                                     to: Student
                                     layout: BaseLayout
                                     sendAutomatically: false
                                     blocks:
                                       - name: Intro
                                         content: Intro
                                       - name: Main
                                         content: Main
                                       - name: Outro
                                         content: Outro
                                     """;

    private const string NotifyTemplate = """
                                          params:
                                            - name: recipient
                                              type: String!
                                            - name: greeting
                                              type: LocalString
                                              default: Hello
                                          subject: ${prefix} for ${recipient}
                                          to: ${recipient}
                                          blocks:
                                            - name: Greeting
                                              content:
                                                en: ${greeting.en} ${recipient}
                                                nl: ${greeting.nl} ${recipient}
                                            - name: Main
                                              content: Template main
                                          """;

    // Blocks

    [Fact]
    public void ModelParser_JoinsBlocksIntoBodyPerLanguage()
    {
        var parser = Parse(new()
        {
            ["Project/Emails/Welcome.yaml"] = """
                                              blocks:
                                                - name: Intro
                                                  content:
                                                    en: Hello
                                                    nl: Hallo
                                                - name: Outro
                                                  content: Bye
                                              """
        });

        var email = GetEmail(parser);

        Assert.Equal("Hello\n\nBye", email.Body!.En);
        Assert.Equal("Hallo\n\nBye", email.Body.Nl);
    }

    [Theory]
    [InlineData(false, false, "Common", "Common")]
    [InlineData(true, false, "Base", "Base")]
    [InlineData(false, true, "Common", "Project")]
    [InlineData(true, true, "Base", "Project")]
    public void ModelParser_ResolvesNamedBlockFromNearestDefinitionBeforeCommon(
        bool inBase, bool inProject, string expectedBase, string expectedProject)
    {
        var files = new Dictionary<string, string>
        {
            ["Common/Templates/EmailBlocks/Signature.yaml"] = "content: Common",
            ["Base/Emails/Welcome.yaml"] = "blocks:\n  - name: Signature"
        };
        if (inBase) files["Base/Templates/EmailBlocks/Signature.yaml"] = "content: Base";
        if (inProject) files["Project/Templates/EmailBlocks/Signature.yaml"] = "content: Project";

        var parser = Parse(files, inheritFromBase: true);

        Assert.Equal(expectedBase, GetEmail(parser, "Base").Body!.En);
        Assert.Equal(expectedProject, GetEmail(parser).Body!.En);
    }

    [Theory]
    [InlineData("body: Plain\nblocks:\n  - name: Intro\n    content: Intro",
        "Email 'Welcome' in 'Project' has both body and content blocks")]
    [InlineData("blocks:\n  - name: Missing",
        "Email block 'Missing' used in 'Welcome' of 'Project' not found")]
    [InlineData("blocks:\n  - name: Intro\n    remove: true",
        "Email 'Welcome' in 'Project' removes block 'Intro' but does not inherit it")]
    [InlineData("blocks:\n  - name: Intro\n    content: A\n  - name: Intro\n    content: B",
        "Email 'Welcome' in 'Project' declares duplicate content block(s): Intro")]
    [InlineData("blocks:\n  - name: Intro\n    content: A\n  - name: Outro\n    content: B\n    after: Intro",
        "Email 'Welcome' in 'Project' places block 'Outro' after 'Intro' but does not inherit it")]
    public void ModelParser_RejectsInvalidBlocks(string email, string expectedMessage)
    {
        var exception = Assert.Throws<Exception>(() =>
            Parse(new() { ["Project/Emails/Welcome.yaml"] = email }));

        Assert.Contains(expectedMessage, exception.Message);
    }

    // Inheritance

    [Theory]
    [InlineData("", "Intro|Main|Outro")]
    [InlineData("blocks:\n  - name: Main\n    content: New", "Intro|New|Outro")]
    [InlineData("blocks:\n  - name: Extra\n    content: Extra", "Intro|Main|Outro|Extra")]
    [InlineData("blocks:\n  - name: Extra\n    content: Extra\n    after: Intro", "Intro|Extra|Main|Outro")]
    [InlineData("blocks:\n  - name: Outro\n    after: Intro", "Intro|Outro|Main")]
    [InlineData("blocks:\n  - name: Main\n    content: New\n    after: Outro", "Intro|Outro|New")]
    [InlineData("blocks:\n  - name: Main\n    remove: true", "Intro|Outro")]
    [InlineData("blocks:\n  - name: Extra\n    content: Extra\n    after: Intro\n  - name: Outro\n    remove: true",
        "Intro|Extra|Main")]
    public void ModelParser_MergesInheritedEmailBlocks(string consumer, string expected)
    {
        var parser = Parse(new()
        {
            ["Base/Emails/Welcome.yaml"] = BaseEmail,
            ["Project/Emails/Welcome.yaml"] = "subject: Override\n" + consumer
        }, inheritFromBase: true);

        Assert.Equal(expected.Replace("|", "\n\n"), GetEmail(parser).Body!.En);
        Assert.Equal("Intro\n\nMain\n\nOutro", GetEmail(parser, "Base").Body!.En);
    }

    [Theory]
    [InlineData("", "Base subject", new[] { "Student" }, "BaseLayout", false)]
    [InlineData("subject: Child subject\nto: Supervisor\nlayout: ChildLayout\nsendAutomatically: true",
        "Child subject", new[] { "Supervisor" }, "ChildLayout", true)]
    [InlineData("to: []\nlayout: null", "Base subject", new string[0], null, false)]
    public void ModelParser_InheritsOverridesOrClearsEmailSettings(
        string consumer, string subject, string[] to, string? layout, bool sendAutomatically)
    {
        var parser = Parse(new()
        {
            ["Base/Emails/Welcome.yaml"] = BaseEmail,
            ["Project/Emails/Welcome.yaml"] = "cc: Reviewer\n" + consumer
        }, inheritFromBase: true);

        var email = GetEmail(parser);

        Assert.Equal(subject, email.Subject!.En);
        Assert.Equal(to, email.To!);
        Assert.Equal(["Reviewer"], email.Cc!);
        Assert.Equal(layout, email.Layout);
        Assert.Equal(sendAutomatically, email.SendAutomatically);
    }

    [Fact]
    public void ModelParser_ClearsInheritedBlocksWithEmptyList()
    {
        var parser = Parse(new()
        {
            ["Base/Emails/Welcome.yaml"] = BaseEmail,
            ["Project/Emails/Welcome.yaml"] = "blocks: []"
        }, inheritFromBase: true);

        var email = GetEmail(parser);

        Assert.Empty(email.ContentBlocks);
        Assert.Null(email.Body);
    }

    [Fact]
    public void ModelParser_ClonesInheritedEmailThatIsNotOverridden()
    {
        var parser = Parse(new() { ["Base/Emails/Welcome.yaml"] = BaseEmail }, inheritFromBase: true);

        var baseEmail = GetEmail(parser, "Base");
        var projectEmail = GetEmail(parser);

        Assert.NotSame(baseEmail, projectEmail);
        Assert.NotSame(baseEmail.ContentBlocks, projectEmail.ContentBlocks);
        Assert.Equal(baseEmail.Body!.En, projectEmail.Body!.En);
    }

    [Theory]
    [InlineData("blocks:\n  - name: Missing\n    remove: true",
        "Email 'Welcome' removes content block 'Missing', which does not exist in the source.")]
    [InlineData("blocks:\n  - name: Extra\n    content: Extra\n    after: Missing",
        "Email 'Welcome' places content block 'Extra' after 'Missing', which does not exist")]
    [InlineData("body: Plain",
        "Email 'Welcome' declares a body, but the email it inherits from uses content blocks")]
    [InlineData("blocks: []\nbody: Plain",
        "Email 'Welcome' declares a body, but the email it inherits from uses content blocks")]
    [InlineData("blocks:\n  - name: Main\n    content: A\n  - name: Main\n    content: B",
        "Email 'Welcome' in 'Project' declares duplicate content block(s): Main")]
    public void ModelParser_RejectsInvalidInheritedBlockChanges(string consumer, string expectedMessage)
    {
        var exception = Assert.Throws<Exception>(() => Parse(new()
        {
            ["Base/Emails/Welcome.yaml"] = BaseEmail,
            ["Project/Emails/Welcome.yaml"] = consumer
        }, inheritFromBase: true));

        Assert.Contains(expectedMessage, exception.Message);
    }

    [Fact]
    public void ModelParser_RejectsNamedBlockWithoutContent()
    {
        var exception = Assert.Throws<Exception>(() => Parse(new()
        {
            ["Common/Templates/EmailBlocks/Empty.yaml"] = "name: Empty",
            ["Project/Emails/Welcome.yaml"] = "blocks:\n  - name: Empty"
        }));

        Assert.Contains("Email block 'Empty' used in 'Welcome' of 'Project' has no content", exception.Message);
    }

    [Fact]
    public void ModelParser_RejectsDuplicateBlockNamesInEmailTemplate()
    {
        var exception = Assert.Throws<Exception>(() => Parse(new()
        {
            ["Common/Templates/Emails/Dup.yaml"] =
                "blocks:\n  - name: A\n    content: One\n  - name: A\n    content: Two",
            ["Project/Emails/Welcome.yaml"] = "template: Dup"
        }));

        Assert.Contains("Email template 'Dup' declares duplicate content block(s): A", exception.Message);
    }

    [Fact]
    public void ModelParser_MergesBlocksAcrossMultipleInheritanceLevels()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases",
            ["Base/Emails/Welcome.yaml"] = BaseEmail,
            ["Mid/Entity.yaml"] = "name: Mid\ntitlePlural: Mids\ninheritsFrom: Base",
            ["Mid/Emails/Welcome.yaml"] = "blocks:\n  - name: Extra\n    content: Extra\n    after: Intro",
            ["Leaf/Entity.yaml"] = "name: Leaf\ntitlePlural: Leaves\ninheritsFrom: Mid",
            ["Leaf/Emails/Welcome.yaml"] = "blocks:\n  - name: Main\n    remove: true"
        }));

        Assert.Equal("Intro\n\nExtra\n\nMain\n\nOutro", GetEmail(parser, "Mid").Body!.En);
        Assert.Equal("Intro\n\nExtra\n\nOutro", GetEmail(parser, "Leaf").Body!.En);
    }

    // Templates

    [Theory]
    [InlineData("Welcome", "values:\n  recipient: Student",
        "Welcome for Student", "Hello Student", "Hello Student")]
    [InlineData("Reminder_Late", "values:\n  recipient: Student\n  greeting:\n    en: Dear\n    nl: Beste",
        "Reminder for Student", "Dear Student", "Beste Student")]
    [InlineData("Welcome", "values:\n  Recipient: Student\n  prefix: Custom",
        "Custom for Student", "Hello Student", "Hello Student")]
    public void ModelParser_SubstitutesEmailTemplateValues(
        string name, string consumer, string subject, string en, string nl)
    {
        var parser = Parse(new()
        {
            ["Common/Templates/Emails/Notify.yaml"] = NotifyTemplate,
            [$"Project/Emails/{name}.yaml"] = "template: Notify\n" + consumer
        });

        var email = GetEmail(parser, name: name);

        Assert.Equal(subject, email.Subject!.En);
        Assert.Equal(["Student"], email.To!);
        Assert.Equal(en + "\n\nTemplate main", email.Body!.En);
        Assert.Equal(nl + "\n\nTemplate main", email.Body.Nl);
        Assert.Equal("Student", email.ResolvedTemplateValues!["recipient"]);
    }

    [Fact]
    public void ModelParser_MergesConsumerOverridesIntoEmailTemplate()
    {
        var parser = Parse(new()
        {
            ["Common/Templates/Emails/Notify.yaml"] = NotifyTemplate,
            ["Project/Emails/Welcome.yaml"] = """
                                              template: Notify
                                              values:
                                                recipient: Student
                                              subject: Custom
                                              blocks:
                                                - name: Main
                                                  content: Custom main
                                                - name: Extra
                                                  content: Extra
                                                  after: Greeting
                                              """
        });

        var email = GetEmail(parser);

        Assert.Equal("Custom", email.Subject!.En);
        Assert.Equal(["Student"], email.To!);
        Assert.Equal("Hello Student\n\nExtra\n\nCustom main", email.Body!.En);
    }

    [Theory]
    [InlineData("", "Template", "ParentLayout")]
    [InlineData("\nsubject: Consumer", "Consumer", "ParentLayout")]
    [InlineData("\nlayout: ConsumerLayout", "Template", "ConsumerLayout")]
    public void ModelParser_ResolvesEmailSettingsWithConsumerThenTemplateThenParentPrecedence(
        string consumer, string expectedSubject, string expectedLayout)
    {
        var parser = Parse(new()
        {
            ["Common/Templates/Emails/Simple.yaml"] = "subject: Template\nsendAutomatically: true",
            ["Base/Emails/Welcome.yaml"] = "subject: Parent\nlayout: ParentLayout\nsendAutomatically: false",
            ["Project/Emails/Welcome.yaml"] = "template: Simple" + consumer
        }, inheritFromBase: true);

        var email = GetEmail(parser);

        Assert.Equal(expectedSubject, email.Subject!.En);
        Assert.Equal(expectedLayout, email.Layout);
        Assert.True(email.SendAutomatically);
    }

    [Theory]
    [InlineData(false, false, "Common")]
    [InlineData(true, false, "Base")]
    [InlineData(true, true, "Project")]
    public void ModelParser_ResolvesEmailTemplateFromNearestDefinitionBeforeCommon(
        bool inBase, bool inProject, string expectedSubject)
    {
        var files = new Dictionary<string, string>
        {
            ["Common/Templates/Emails/Simple.yaml"] = "subject: Common",
            ["Project/Emails/Welcome.yaml"] = "template: Simple"
        };
        if (inBase) files["Base/Templates/Emails/Simple.yaml"] = "subject: Base";
        if (inProject) files["Project/Templates/Emails/Simple.yaml"] = "subject: Project";

        var parser = Parse(files, inheritFromBase: true);

        Assert.Equal(expectedSubject, GetEmail(parser).Subject!.En);
    }

    [Fact]
    public void ModelParser_InheritsResolvedTemplateValuesFromParentEmail()
    {
        var parser = Parse(new()
        {
            ["Common/Templates/Emails/Notify.yaml"] = NotifyTemplate,
            ["Base/Emails/Welcome.yaml"] = "template: Notify\nvalues:\n  recipient: Student",
            ["Project/Emails/Welcome.yaml"] = "subject: Child"
        }, inheritFromBase: true);

        var email = GetEmail(parser);

        Assert.Equal("Child", email.Subject!.En);
        Assert.Equal("Hello Student\n\nTemplate main", email.Body!.En);
        Assert.Equal("Student", email.ResolvedTemplateValues!["recipient"]);
    }

    [Theory]
    [InlineData("template: Notify", "Missing required template parameter 'recipient' for 'Welcome'")]
    [InlineData("template: Notify\nvalues:\n  recipient: Student\n  bogus: x",
        "Unknown template parameters 'bogus' for 'Welcome'")]
    public void ModelParser_RejectsInvalidEmailTemplateValues(string consumer, string expectedMessage)
    {
        var exception = Assert.Throws<ArgumentException>(() => Parse(new()
        {
            ["Common/Templates/Emails/Notify.yaml"] = NotifyTemplate,
            ["Project/Emails/Welcome.yaml"] = consumer
        }));

        Assert.Contains(expectedMessage, exception.Message);
    }

    [Fact]
    public void ModelParser_RejectsBilingualValueOutsideLanguageKey()
    {
        var exception = Assert.Throws<ArgumentException>(() => Parse(new()
        {
            ["Common/Templates/Emails/Greet.yaml"] =
                "params:\n  - name: greeting\n    type: LocalString!\nsubject: ${greeting}",
            ["Project/Emails/Welcome.yaml"] = "template: Greet\nvalues:\n  greeting: Hi"
        }));

        Assert.Contains("uses bilingual value '${greeting}' outside an 'en'/'nl' key", exception.Message);
    }

    [Fact]
    public void ModelParser_RejectsUnknownEmailTemplate()
    {
        var exception = Assert.Throws<Exception>(() =>
            Parse(new() { ["Project/Emails/Welcome.yaml"] = "template: Missing" }));

        Assert.Contains("'Welcome' in definition 'Project' references unknown template 'Missing'",
            exception.Message);
    }

    [Fact]
    public void ModelParser_RejectsDuplicateEmailNames()
    {
        var exception = Assert.Throws<Exception>(() => Parse(new()
        {
            ["Project/Emails/A.yaml"] = "name: Welcome\nsubject: A",
            ["Project/Emails/B.yaml"] = "name: Welcome\nsubject: B"
        }));

        Assert.Contains("Definition 'Project' declares duplicate email(s): Welcome", exception.Message);
    }

    [Fact]
    public void ModelParser_InheritsAllUndeclaredEmailSettings()
    {
        var parser = Parse(new()
        {
            ["Base/Emails/Welcome.yaml"] = """
                                           subject: Base
                                           body: Base body
                                           to: Student
                                           cc: Reviewer
                                           bcc: Coordinator
                                           sendAsMail: true
                                           buttons:
                                             - url: https://example.org
                                               label: Open
                                           attachments:
                                             - template: Report
                                           """,
            ["Project/Emails/Welcome.yaml"] = "subject: Child"
        }, inheritFromBase: true);

        var email = GetEmail(parser);

        Assert.Equal("Child", email.Subject!.En);
        Assert.Equal("Base body", email.Body!.En);
        Assert.Equal(new[] { "Reviewer" }, email.Cc!);
        Assert.Equal(new[] { "Coordinator" }, email.Bcc!);
        Assert.True(email.SendAsMail);
        Assert.Equal("https://example.org", Assert.Single(email.Buttons).Url);
        Assert.Equal("Report", Assert.Single(email.Attachments).Template);
    }

    [Fact]
    public void ModelParser_PrefersOwnTemplateValuesOverInheritedOnes()
    {
        var parser = Parse(new()
        {
            ["Common/Templates/Emails/Notify.yaml"] = NotifyTemplate,
            ["Base/Emails/Welcome.yaml"] = "template: Notify\nvalues:\n  recipient: Student",
            ["Project/Emails/Welcome.yaml"] = "template: Notify\nvalues:\n  recipient: Supervisor"
        }, inheritFromBase: true);

        var email = GetEmail(parser);

        Assert.Equal("Supervisor", email.ResolvedTemplateValues!["recipient"]);
        Assert.Equal(new[] { "Supervisor" }, email.To!);
        Assert.Equal("Hello Supervisor\n\nTemplate main", email.Body!.En);
    }

// Documents current behaviour (finding 3): template blocks are merged on top of the parent's blocks.
    [Fact]
    public void ModelParser_MergesTemplateBlocksWithInheritedBlocks()
    {
        var parser = Parse(new()
        {
            ["Common/Templates/Emails/Simple.yaml"] =
                "blocks:\n  - name: Main\n    content: Template main\n  - name: Extra\n    content: Extra",
            ["Base/Emails/Welcome.yaml"] = BaseEmail,
            ["Project/Emails/Welcome.yaml"] = "template: Simple"
        }, inheritFromBase: true);

        Assert.Equal("Intro\n\nTemplate main\n\nOutro\n\nExtra", GetEmail(parser).Body!.En);
    }

    [Fact]
    public void ModelParser_RejectsOptionalTemplateValueWithoutDefault()
    {
        var exception = Assert.Throws<ArgumentException>(() => Parse(new()
        {
            ["Common/Templates/Emails/Note.yaml"] = "params:\n  - name: note\n    type: String\nsubject: ${note}",
            ["Project/Emails/Welcome.yaml"] = "template: Note"
        }));

        Assert.Contains("Missing template value 'note' for template 'Note'", exception.Message);
    }

    private static ModelParser Parse(Dictionary<string, string> files, bool inheritFromBase = false)
    {
        files.TryAdd("Project/Entity.yaml", inheritFromBase
            ? "name: Project\ntitlePlural: Projects\ninheritsFrom: Base"
            : "name: Project\ntitlePlural: Projects");
        if (inheritFromBase)
            files.TryAdd("Base/Entity.yaml", "name: Base\ntitlePlural: Bases");

        return new ModelParser(new DictionaryProvider(files));
    }

    private static NamedEmail GetEmail(ModelParser parser, string definition = "Project", string name = "Welcome")
        => parser.WorkflowDefinitions[definition].Emails.Single(e => e.Name == name);
}