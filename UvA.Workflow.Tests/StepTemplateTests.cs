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

    [Fact]
    public void ModelParser_InheritsMappingValuesFromParentTemplate()
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
                "name: Start\ntemplate: Parent\nvalues:\n  caption:\n    en: Proposal\n    nl: Voorstel",
            ["Project/Steps/Submit.yaml"] = "name: Start_Submit\ntemplate: Submit"
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