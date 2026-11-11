using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class FormTemplateTests
{
    [Theory]
    [InlineData("", "Default", "Default")]
    [InlineData("values:\n  caption: Proposal", "Proposal", "Proposal")]
    [InlineData(
        "values:\n  caption:\n    en: Proposal\n    nl: Voorstel",
        "Proposal",
        "Voorstel")]
    public void ModelParser_SubstitutesFormTemplateValues(
        string consumer, string en, string nl)
    {
        var workflow = Parse("""
                             name: Review
                             params:
                               - name: caption
                                 type: LocalString!
                                 default: Default
                             title: ${prefix}
                             pages:
                               - name: Main
                                 elements:
                                   - text:
                                       en: Review ${caption.en}
                                       nl: Review ${caption.nl}
                             """, consumer);

        var form = workflow.Forms.Single(f => f.Name == "Edit");
        var element = Assert.Single(Assert.Single(form.Pages).PageElements);

        Assert.Equal("Edit", form.Title!.En);
        Assert.Equal("Review " + en, element.Text!.En);
        Assert.Equal("Review " + nl, element.Text!.Nl);
    }

    [Theory]
    [InlineData("", "TemplateSubmit", "TemplateSave", false, "TemplateSubmit")]
    [InlineData(
        "onSubmit:\n  - event: ConsumerSubmit\n" +
        "onSave:\n  - event: ConsumerSave\n" +
        "emitFormSubmitEvent: true\n" +
        "submittedWhenEvents: [ConsumerSubmit]",
        "ConsumerSubmit", "ConsumerSave", true, "ConsumerSubmit")]
    [InlineData(
        "onSubmit: []\nonSave: []\n" +
        "emitFormSubmitEvent: true\nsubmittedWhenEvents: null",
        null, null, true, null)]
    public void ModelParser_ResolvesFormSettingsWithConsumerThenTemplateThenParentPrecedence(
        string consumer,
        string? submitEvent,
        string? saveEvent,
        bool emitFormSubmitEvent,
        string? submittedWhenEvent)
    {
        var workflow = Parse("""
                             name: Review
                             onSubmit:
                               - event: TemplateSubmit
                             onSave:
                               - event: TemplateSave
                             emitFormSubmitEvent: false
                             submittedWhenEvents: [TemplateSubmit]
                             """, consumer, """
                                            name: Edit
                                            onSubmit:
                                              - event: ParentSubmit
                                            onSave:
                                              - event: ParentSave
                                            emitFormSubmitEvent: true
                                            submittedWhenEvents: [ParentSubmit]
                                            """);

        var form = workflow.Forms.Single(f => f.Name == "Edit");

        Assert.Equal(
            submitEvent == null ? Array.Empty<string>() : new[] { submitEvent },
            form.OnSubmit.Select(effect => effect.Event));
        Assert.Equal(
            saveEvent == null ? Array.Empty<string>() : new[] { saveEvent },
            form.OnSave.Select(effect => effect.Event));
        Assert.Equal(emitFormSubmitEvent, form.EmitFormSubmitEvent);

        if (submittedWhenEvent == null)
            Assert.Null(form.SubmittedWhenEvents);
        else
            Assert.Equal(new[] { submittedWhenEvent }, form.SubmittedWhenEvents);

        foreach (var eventName in new[] { submitEvent, saveEvent }.OfType<string>())
            Assert.Contains(workflow.Events, evt => evt.Name == eventName);
    }

    [Theory]
    [InlineData("", "Template page", false)]
    [InlineData(
        "pages:\n" +
        "  - name: Main\n    title: Consumer page\n" +
        "  - name: Extra\n    title: Extra page",
        "Consumer page", true)]
    [InlineData("pages: []", null, false)]
    public void ModelParser_MergesOverridesOrClearsTemplatePages(
        string consumer, string? expectedTitle, bool hasExtraPage)
    {
        var workflow = Parse("""
                             name: Review
                             pages:
                               - name: Main
                                 title: Template page
                             """, consumer, """
                                            name: Edit
                                            pages:
                                              - name: Main
                                                title: Parent page
                                            """);

        var form = workflow.Forms.Single(f => f.Name == "Edit");

        if (expectedTitle == null)
        {
            // Preprocessing creates a default page when no pages remain.
            Assert.DoesNotContain(form.Pages, page => page.Name == "Main");
            Assert.Empty(Assert.Single(form.Pages).PageElements);
        }
        else
        {
            Assert.Equal(
                expectedTitle,
                Assert.Single(form.Pages, page => page.Name == "Main").Title!.En);
        }

        Assert.Equal(hasExtraPage, form.Pages.Any(page => page.Name == "Extra"));
        Assert.Equal(hasExtraPage ? 2 : 1, form.Pages.Count);
    }

    [Fact]
    public void ModelParser_RejectsMissingRequiredFormTemplateValue()
    {
        var exception = Assert.Throws<ArgumentException>(() => Parse("""
                                                                     name: Review
                                                                     params:
                                                                       - name: caption
                                                                         type: String!
                                                                     title: ${caption}
                                                                     """, ""));

        Assert.Contains(
            "Missing required template parameter 'caption' for 'Edit'",
            exception.Message);
    }

    [Theory]
    [InlineData("", "First", "View")]
    [InlineData("property: Second\ntargetForm: Other", "Second", "Other")]
    [InlineData("property: null\ntargetForm: null", null, null)]
    public void ModelParser_InheritsOverridesOrClearsReferenceFormConfiguration(
        string consumer, string? propertyName, string? targetFormName)
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Context/Entity.yaml"] = "name: Context\ntitlePlural: Contexts",
            ["Context/Forms/View.yaml"] = "name: View",
            ["Context/Forms/Other.yaml"] = "name: Other",
            ["Project/Entity.yaml"] = """
                                      name: Project
                                      titlePlural: Projects
                                      properties:
                                        - name: First
                                          type: Context
                                        - name: Second
                                          type: Context
                                      """,
            ["Common/Templates/Forms/Review.yaml"] = """
                                                     name: Review
                                                     property: First
                                                     targetForm: View
                                                     """,
            ["Project/Forms/Edit.yaml"] =
                "name: Edit\ntemplate: Review\n" + consumer
        }));

        var form = parser.WorkflowDefinitions["Project"].Forms.Single();

        Assert.Equal(propertyName, form.PropertyName);
        Assert.Equal(targetFormName, form.TargetFormName);

        if (targetFormName == null)
            Assert.Null(form.TargetForm);
        else
            Assert.Same(
                parser.WorkflowDefinitions["Context"].Forms.Single(target => target.Name == targetFormName),
                form.TargetForm);
    }

    private static WorkflowDefinition Parse(
        string template, string consumer, string? inheritedForm = null)
    {
        var files = new Dictionary<string, string>
        {
            ["Common/Templates/Forms/Review.yaml"] = template,
            ["Project/Entity.yaml"] = "name: Project\ntitlePlural: Projects",
            ["Project/Forms/Edit.yaml"] =
                "name: Edit\ntemplate: Review\n" + consumer
        };

        if (inheritedForm != null)
        {
            files["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases";
            files["Base/Forms/Edit.yaml"] = inheritedForm;
            files["Project/Entity.yaml"] += "\ninheritsFrom: Base";
        }

        return new ModelParser(new DictionaryProvider(files))
            .WorkflowDefinitions["Project"];
    }
}