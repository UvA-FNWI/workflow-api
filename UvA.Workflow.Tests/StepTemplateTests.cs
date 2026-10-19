using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class StepTemplateTests
{
    [Fact]
    public void ModelParser_SubstitutesTemplateValues()
    {
        var parser = Parse("values:\n  caption: Proposal");

        var step = parser.WorkflowDefinitions["Project"].Steps.Single();
        Assert.Equal("Working on Proposal", step.Progress.Single().Text!.En);
    }

    [Theory]
    [InlineData("values:\n  caption: First\n  CAPTION: Second")]
    [InlineData("values:\n  caption: First\n  caption: Second")]
    public void ModelParser_RejectsDuplicateTemplateValues(string values)
    {
        var exception = Assert.ThrowsAny<Exception>(() => Parse(values));

        Assert.Contains("caption", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("values:\n  caption: Proposal\n  unknown: Other")]
    public void ModelParser_RejectsUnknownTemplateValues(string values)
    {
        var exception = Assert.Throws<ArgumentException>(() => Parse(values));

        Assert.Contains("Unknown template parameters 'unknown'", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("values: {}")]
    [InlineData("values: null")]
    public void ModelParser_RejectsMissingRequiredTemplateValue(string values)
    {
        var exception = Assert.Throws<ArgumentException>(() => Parse(values));

        Assert.Contains("Missing required template parameter 'caption'", exception.Message);
    }

    [Theory]
    [InlineData("values: invalid")]
    [InlineData("values: \"null\"")]
    [InlineData("values: []")]
    [InlineData("values:\n  - name: caption\n    value: Proposal")]
    public void ModelParser_RejectsNonMappingTemplateValues(string values)
    {
        var exception = Assert.Throws<Exception>(() => Parse(values));

        Assert.Contains("Failed to parse Project/Steps/Start.yaml", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModelParser_InheritsMappingValuesFromParentTemplate(bool overrideChildren)
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Common/StepTemplates/Parent.yaml"] = """
                                                   name: Parent
                                                   params:
                                                     - name: caption
                                                       type: LocalString!
                                                   children: ["${prefix}_Submit"]
                                                   """,
            ["Common/StepTemplates/Submit.yaml"] = """
                                                   name: Submit
                                                   params:
                                                     - name: caption
                                                       type: LocalString!
                                                   progress:
                                                     - text:
                                                         en: Working on ${caption.en}
                                                         nl: Werkt aan ${caption.nl}
                                                   """,
            ["Project/Entity.yaml"] = "name: Project\ntitlePlural: Projects\nsteps: [Start]",
            ["Project/Steps/Start.yaml"] =
                "name: Start\ntemplate: Parent\nvalues:\n  caption:\n    en: Proposal\n    nl: Voorstel\n"
                + (overrideChildren ? "children: [Start_Alternative]" : ""),
            ["Project/Steps/Submit.yaml"] =
                $"name: {(overrideChildren ? "Start_Alternative" : "Start_Submit")}\ntemplate: Submit"
        }));

        var child = parser.WorkflowDefinitions["Project"].Steps.Single().Children.Single();
        Assert.Equal("Working on Proposal", child.Progress.Single().Text!.En);
        Assert.Equal("Werkt aan Voorstel", child.Progress.Single().Text!.Nl);
    }

    [Theory]
    [InlineData("caption:\n    en: Proposal\n    nl: Voorstel", "Working on Proposal", "Werkt aan Voorstel")]
    [InlineData("caption: Proposal", "Working on Proposal", "Werkt aan Proposal")]
    [InlineData("caption:\n    en: Proposal", "Working on Proposal", "Werkt aan Proposal")]
    [InlineData("caption:\n    en: \"Student's proposal: draft\"\n    nl: \"Voorstel: concept\"",
        "Working on Student's proposal: draft", "Werkt aan Voorstel: concept")]
    public void ModelParser_SubstitutesLocalizedTemplateValues(string value, string en, string nl)
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Common/StepTemplates/Product.yaml"] = """
                                                    name: Product
                                                    params:
                                                      - name: caption
                                                        type: LocalString!
                                                    progress:
                                                      - text:
                                                          en: Working on ${caption.en}
                                                          nl: Werkt aan ${caption.nl}
                                                        header:
                                                          label:
                                                            en: Review ${caption.en}
                                                            nl: Review ${caption.nl}
                                                    """,
            ["Project/Entity.yaml"] = "name: Project\ntitlePlural: Projects\nsteps: [Start]",
            ["Project/Steps/Start.yaml"] = "name: Start\ntemplate: Product\nvalues:\n  " + value
        }));
        var progress = parser.WorkflowDefinitions["Project"].Steps.Single().Progress;
        Assert.Equal(en, progress[0].Text!.En);
        Assert.Equal(nl, progress[0].Text!.Nl);
        Assert.Equal("Review " + en["Working on ".Length..], progress[0].Header!.Label!.En);
        Assert.Equal("Review " + nl["Werkt aan ".Length..], progress[0].Header!.Label!.Nl);
        Assert.Equal(DataType.LocalString, new StepTemplateParameter { Type = "LocalString!" }.DataType);
    }

    [Fact]
    public void ModelParser_PreservesConsumerAlongsideRules()
    {
        var workflow = ParseTemplatedWorkflow("name: Product", "mode: Alongside\nbefore: FinalAssessment");
        var step = workflow.AllSteps.Single(s => s.Name == "Start");

        Assert.Equal(StepMode.Alongside, step.Mode);
        Assert.Equal("FinalAssessment", step.Before);
    }

    [Fact]
    public void ModelParser_MergesConsumerActionsAndOverridesCompletionCondition()
    {
        var workflow = ParseTemplatedWorkflow("""
                                              name: Product
                                              actions:
                                                - roles: [Coordinator]
                                                  type: Submit
                                                  form: Approve
                                                - roles: [Coordinator]
                                                  type: Submit
                                                  form: Revise
                                              ends:
                                                logical:
                                                  operator: Or
                                                  children:
                                                    - event: Approve
                                                    - event: Revise
                                              """, """
                                                   actions:
                                                     - roles: [Coordinator]
                                                       type: Submit
                                                       form: Reject
                                                   ends:
                                                     logical:
                                                       operator: Or
                                                       children:
                                                         - event: Approve
                                                         - event: Revise
                                                         - event: Reject
                                                   """);
        var step = workflow.AllSteps.Single(s => s.Name == "Start");

        Assert.Equal(["Approve", "Revise", "Reject"], step.Actions.Select(a => a.Form));
        Assert.Equal(["Approve", "Revise", "Reject"],
            workflow.Roles.Single(r => r.Name == "Coordinator").Actions.Select(a => a.Form));
        Assert.Equal(["Approve", "Revise", "Reject"], step.Ends!.Logical!.Children.Select(c => c.Event!.Id));
    }

    [Theory]
    [InlineData("", "Assessment", "Beoordeling")]
    [InlineData("title: Custom", "Custom", "Custom")]
    [InlineData("title: null", null, null)]
    public void ModelParser_UsesTemplateTitleUnlessConsumerDeclaresTitle(string consumer, string? en, string? nl)
    {
        var workflow = ParseTemplatedWorkflow("""
                                              name: Product
                                              title:
                                                en: Assessment
                                                nl: Beoordeling
                                              """, consumer);
        var step = workflow.AllSteps.Single(s => s.Name == "Start");

        Assert.Equal(en, step.Title?.En);
        Assert.Equal(nl, step.Title?.Nl);
    }

    [Theory]
    [InlineData("actions: []", 0)]
    [InlineData("actions:\n  - name: Approve\n    roles: [Coordinator]\n    type: Submit\n    form: Reject", 1)]
    public void ModelParser_HonorsConsumerActionClearingAndNamedOverrides(string consumer, int count)
    {
        var workflow = ParseTemplatedWorkflow("""
                                              name: Product
                                              actions:
                                                - name: Approve
                                                  roles: [Coordinator]
                                                  type: Submit
                                                  form: Approve
                                              """, consumer);
        var step = workflow.AllSteps.Single(s => s.Name == "Start");

        Assert.Equal(count, step.Actions.Count);
        if (count > 0)
            Assert.Equal("Reject", step.Actions.Single().Form);
    }

    [Fact]
    public void ModelParser_PreservesTemplateAndConsumerDeclarationsDuringWorkflowInheritance()
    {
        var workflow = ParseTemplatedWorkflow("""
                                              name: Product
                                              title: Template title
                                              mode: Normal
                                              progress: []
                                              """, "before: FinalAssessment", """
                                                                              name: Start
                                                                              title: Parent title
                                                                              mode: Alongside
                                                                              before: Start
                                                                              progress:
                                                                                - text: Parent progress
                                                                              """);
        var step = workflow.AllSteps.Single(s => s.Name == "Start");

        Assert.Equal("Template title", step.Title!.En);
        Assert.Equal(StepMode.Normal, step.Mode);
        Assert.Equal("FinalAssessment", step.Before);
        Assert.Empty(step.Progress);
    }

    private static WorkflowDefinition ParseTemplatedWorkflow(string template, string consumer,
        string? inheritedStep = null)
    {
        var files = new Dictionary<string, string>
        {
            ["Common/StepTemplates/Product.yaml"] = template,
            ["Common/Roles/Coordinator.yaml"] = "name: Coordinator",
            ["Project/Entity.yaml"] = "name: Project\ntitlePlural: Projects\nsteps: [Start, FinalAssessment]",
            ["Project/Steps/Start.yaml"] = "name: Start\ntemplate: Product\n" + consumer,
            ["Project/Steps/FinalAssessment.yaml"] = "name: FinalAssessment",
            ["Project/Forms/Approve.yaml"] = "name: Approve",
            ["Project/Forms/Revise.yaml"] = "name: Revise",
            ["Project/Forms/Reject.yaml"] = "name: Reject"
        };
        if (inheritedStep != null)
        {
            files["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases\nsteps: [Start]";
            files["Base/Steps/Start.yaml"] = inheritedStep;
            files["Project/Entity.yaml"] += "\ninheritsFrom: Base";
        }

        return new ModelParser(new DictionaryProvider(files)).WorkflowDefinitions["Project"];
    }

    private static ModelParser Parse(string values)
        => new(new DictionaryProvider(new()
        {
            ["Common/StepTemplates/Product.yaml"] = """
                                                    name: Product
                                                    params:
                                                      - name: caption
                                                        type: String!
                                                    progress:
                                                      - text: Working on ${caption}
                                                    """,
            ["Project/Entity.yaml"] = "name: Project\ntitlePlural: Projects\nsteps: [Start]",
            ["Project/Steps/Start.yaml"] = "name: Start\ntemplate: Product\n" + values
        }));
}