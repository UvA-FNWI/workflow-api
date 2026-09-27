using UvA.Workflow.Tools;

namespace UvA.Workflow.Tests;

public class PageElementTests
{
    private static Page Page(WorkflowDefinition def, string formName, string pageName) =>
        def.Forms.Get(formName).Pages.Single(p => p.Name == pageName);

    [Fact]
    public void PageElement_WithoutAnyType_Throws()
    {
        var provider = new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases",
            ["Base/Forms/Edit.yaml"] = "name: Edit\npages:\n  - name: P\n    elements:\n      - {}"
        });

        Assert.Throws<Exception>(() => new ModelParser(provider));
    }

    [Fact]
    public void PageElement_WithQuestionAndText_Throws()
    {
        var provider = new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] =
                "name: Base\ntitlePlural: Bases\nproperties:\n  - name: Foo\n    type: String",
            ["Base/Forms/Edit.yaml"] =
                "name: Edit\npages:\n  - name: P\n    elements:\n      - question: Foo\n        text: Some text"
        });

        Assert.Throws<Exception>(() => new ModelParser(provider));
    }

    [Fact]
    public void PageElement_WithUnknownQuestion_Throws()
    {
        var provider = new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases",
            ["Base/Forms/Edit.yaml"] = "name: Edit\npages:\n  - name: P\n    elements:\n      - question: DoesNotExist"
        });

        Assert.Throws<Exception>(() => new ModelParser(provider));
    }

    [Fact]
    public void PageElement_WithKnownQuestion_ResolvesQuestionDefinition()
    {
        var provider = new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] =
                "name: Base\ntitlePlural: Bases\nproperties:\n  - name: Foo\n    type: String",
            ["Base/Forms/Edit.yaml"] = "name: Edit\npages:\n  - name: P\n    elements:\n      - question: Foo"
        });

        var parser = new ModelParser(provider);

        var element = Page(parser.WorkflowDefinitions["Base"], "Edit", "P").PageElements.Single();
        Assert.NotNull(element.QuestionDefinition);
        Assert.Equal("Foo", element.QuestionDefinition!.Name);
    }

    [Fact]
    public void PageElement_TextOnly_DoesNotRequireQuestionResolution()
    {
        var provider = new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases",
            ["Base/Forms/Edit.yaml"] =
                "name: Edit\npages:\n  - name: P\n    elements:\n      - text:\n          en: Hello\n          nl: Hallo"
        });

        var parser = new ModelParser(provider);

        var element = Page(parser.WorkflowDefinitions["Base"], "Edit", "P").PageElements.Single();
        Assert.Null(element.QuestionDefinition);
        Assert.Equal("Hello", element.Text!.En);
    }

    [Fact]
    public void PageElement_CalloutOnly_DoesNotRequireQuestionResolution()
    {
        var provider = new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases",
            ["Base/Forms/Edit.yaml"] =
                "name: Edit\npages:\n  - name: P\n    elements:\n      - callout:\n          title: Note\n          text: Something"
        });

        var parser = new ModelParser(provider);

        var element = Page(parser.WorkflowDefinitions["Base"], "Edit", "P").PageElements.Single();
        Assert.Null(element.QuestionDefinition);
        Assert.NotNull(element.Callout);
    }

    [Fact]
    public void Form_WithoutPagesOrTargetForm_GeneratesSinglePageWithAllPropertiesAsQuestions()
    {
        var provider = new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] =
                "name: Base\ntitlePlural: Bases\nproperties:\n  - name: Foo\n    type: String\n  - name: Bar\n    type: String",
            ["Base/Forms/Edit.yaml"] = "name: Edit"
        });

        var parser = new ModelParser(provider);

        var form = parser.WorkflowDefinitions["Base"].Forms.Get("Edit");
        var page = Assert.Single(form.Pages);
        Assert.Equal(["Foo", "Bar"], page.PageElements.Select(e => e.Question));
    }

    [Fact]
    public void Page_HasResults_TrueWhenAQuestionHasCalculationWeight()
    {
        var provider = new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = """
                                   name: Base
                                   titlePlural: Bases
                                   properties:
                                     - name: Quality
                                       type: Double
                                       calculation:
                                         weight: 1
                                     - name: Comments
                                       type: String
                                   """,
            ["Base/Forms/Edit.yaml"] =
                "name: Edit\npages:\n  - name: P\n    elements:\n      - question: Quality\n      - question: Comments"
        });

        var parser = new ModelParser(provider);
        var page = Page(parser.WorkflowDefinitions["Base"], "Edit", "P");

        Assert.True(page.HasResults);
    }

    [Fact]
    public void Page_HasResults_FalseWhenNoQuestionHasCalculationWeight()
    {
        var provider = new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] =
                "name: Base\ntitlePlural: Bases\nproperties:\n  - name: Comments\n    type: String",
            ["Base/Forms/Edit.yaml"] = "name: Edit\npages:\n  - name: P\n    elements:\n      - question: Comments"
        });

        var parser = new ModelParser(provider);
        var page = Page(parser.WorkflowDefinitions["Base"], "Edit", "P");

        Assert.False(page.HasResults);
    }

    [Fact]
    public void Form_PropertyDefinitions_ReturnsDistinctQuestionsAcrossPages()
    {
        var provider = new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] =
                "name: Base\ntitlePlural: Bases\nproperties:\n  - name: Foo\n    type: String\n  - name: Bar\n    type: String",
            ["Base/Forms/Edit.yaml"] = """
                                       name: Edit
                                       pages:
                                         - name: P1
                                           elements:
                                             - question: Foo
                                             - question: Bar
                                         - name: P2
                                           elements:
                                             - question: Foo
                                             - text:
                                                 en: Hello
                                                 nl: Hallo
                                       """
        });

        var parser = new ModelParser(provider);
        var form = parser.WorkflowDefinitions["Base"].Forms.Get("Edit");

        Assert.Equal(["Foo", "Bar"], form.PropertyDefinitions.Select(p => p.Name));
    }
}