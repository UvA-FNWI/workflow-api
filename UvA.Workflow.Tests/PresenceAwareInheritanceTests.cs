using UvA.Workflow.Events;
using UvA.Workflow.Tools;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests;

public class PresenceAwareInheritanceTests
{
    private static Step Step(WorkflowDefinition def, string name) => def.AllSteps.Single(s => s.Name == name);

    [Theory]
    [InlineData(StepMode.Normal, false, false)]
    [InlineData(StepMode.Alongside, true, true)]
    [InlineData(StepMode.Optional, true, false)]
    public void StepMode_DescribesAlongsideAndBlockingBehavior(StepMode mode, bool isAlongside, bool blocksWorkflow)
    {
        var step = new Step { Mode = mode };

        Assert.Equal(isAlongside, step.IsAlongside);
        Assert.Equal(blocksWorkflow, step.BlocksWorkflow);
    }

    [Fact]
    public void StepMode_IsParsedAndInherited()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases\nsteps:\n  - S",
            ["Base/Steps/S.yaml"] = "name: S\nmode: Optional",
            ["Child/Entity.yaml"] = "name: Child\ntitlePlural: Children\ninheritsFrom: Base"
        }));

        Assert.Equal(StepMode.Optional, Step(parser.WorkflowDefinitions["Base"], "S").Mode);
        Assert.Equal(StepMode.Optional, Step(parser.WorkflowDefinitions["Child"], "S").Mode);
    }

    [Fact]
    public void ChildrenLayout_IsParsedAndInheritedWhenStepIsOverridden()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases\nsteps:\n  - S",
            ["Base/Steps/S.yaml"] = "name: S\nchildrenLayout: CollapsibleRows\nchildren:\n  - A",
            ["Base/Steps/A.yaml"] = "name: A",
            ["Child/Entity.yaml"] = "name: Child\ntitlePlural: Children\ninheritsFrom: Base",
            ["Child/Steps/S.yaml"] = "name: S\ntitle: New title"
        }));

        Assert.Equal(StepChildrenLayout.CollapsibleRows, Step(parser.WorkflowDefinitions["Base"], "S").ChildrenLayout);
        Assert.Equal(StepChildrenLayout.CollapsibleRows, Step(parser.WorkflowDefinitions["Child"], "S").ChildrenLayout);
        Assert.Equal(StepChildrenLayout.Combined, Step(parser.WorkflowDefinitions["Base"], "A").ChildrenLayout);
    }

    [Fact]
    public void ConditionallySkippedAlongsideStep_IsNeitherActiveNorABlocker()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Project/Entity.yaml"] =
                "name: Project\ntitlePlural: Projects\nsteps:\n  - Start\n  - Contract\n  - Assessment",
            ["Project/Steps/Start.yaml"] = "name: Start\nends:\n  event: Started",
            ["Project/Steps/Contract.yaml"] =
                "name: Contract\nmode: Alongside\nbefore: Assessment\ncondition:\n  event: ContractRequired",
            ["Project/Steps/Assessment.yaml"] = "name: Assessment"
        }));
        var service = new ModelService(parser);
        var instance = new WorkflowInstance
        {
            WorkflowDefinition = "Project",
            Properties = [],
            Events = new Dictionary<string, InstanceEvent>
            {
                ["Started"] = new() { Id = "Started", Date = DateTime.UtcNow }
            }
        };

        Assert.Equal("Assessment", service.FindOpenStep(instance)?.Name);
        Assert.Equal(["Assessment"], service.GetActiveSteps(instance));
    }

    [Fact]
    public void Step_ExplicitDefaultsAndEmptyList_WinOverParent()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases\nsteps:\n  - S",
            ["Base/Steps/S.yaml"] = "name: S\ntitle: BaseTitle\nhierarchyMode: Parallel\nchildren:\n  - A\n  - B",
            ["Base/Steps/A.yaml"] = "name: A",
            ["Base/Steps/B.yaml"] = "name: B",
            ["Explicit/Entity.yaml"] = "name: Explicit\ntitlePlural: Es\ninheritsFrom: Base",
            ["Explicit/Steps/S.yaml"] = "name: S\nhierarchyMode: Sequential\nchildren: []",
            ["Omit/Entity.yaml"] = "name: Omit\ntitlePlural: Os\ninheritsFrom: Base",
            ["Omit/Steps/S.yaml"] = "name: S\ntitle: NewTitle"
        }));

        var explicitStep = Step(parser.WorkflowDefinitions["Explicit"], "S");
        Assert.Equal(StepHierarchyMode.Sequential, explicitStep.HierarchyMode);
        Assert.Empty(explicitStep.ChildNames);
        Assert.Equal("BaseTitle", explicitStep.Title!.En);

        var omittedStep = Step(parser.WorkflowDefinitions["Omit"], "S");
        Assert.Equal(StepHierarchyMode.Parallel, omittedStep.HierarchyMode);
        Assert.Equal(["A", "B"], omittedStep.ChildNames);
        Assert.Equal("NewTitle", omittedStep.Title!.En);
    }

    [Fact]
    public void Events_MergeByName_ButExplicitEmptyClears()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases\nevents:\n  - name: E1",
            ["Merge/Entity.yaml"] = "name: Merge\ntitlePlural: Ms\ninheritsFrom: Base\nevents:\n  - name: E2",
            ["Clear/Entity.yaml"] = "name: Clear\ntitlePlural: Cs\ninheritsFrom: Base\nevents: []"
        }));

        var merge = parser.WorkflowDefinitions["Merge"].Events.Select(e => e.Name).ToArray();
        Assert.Contains("E1", merge);
        Assert.Contains("E2", merge);

        Assert.DoesNotContain("E1", parser.WorkflowDefinitions["Clear"].Events.Select(e => e.Name));
    }

    [Fact]
    public void StepActions_MergeUnlessClearedAndKeepAuthorizationInSync()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Common/Roles/Reviewer.yaml"] = "name: Reviewer",
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases\nsteps:\n  - Approval",
            ["Base/Steps/Approval.yaml"] =
                "name: Approval\ntitle: Approval\nactions:\n  - name: Approve\n    type: Execute\n    roles: [Reviewer]",
            ["Omit/Entity.yaml"] = "name: Omit\ntitlePlural: Os\ninheritsFrom: Base",
            ["Omit/Steps/Approval.yaml"] = "name: Approval\ntitle: Approved?",
            ["Merge/Entity.yaml"] = "name: Merge\ntitlePlural: Ms\ninheritsFrom: Base",
            ["Merge/Steps/Approval.yaml"] =
                "name: Approval\nactions:\n  - name: Approve\n    label: Child approval\n    type: Execute\n    roles: [Reviewer]\n  - name: Reject\n    type: Execute\n    roles: [Reviewer]",
            ["Clear/Entity.yaml"] = "name: Clear\ntitlePlural: Cs\ninheritsFrom: Base",
            ["Clear/Steps/Approval.yaml"] = "name: Approval\nactions: []"
        }));

        Role ReviewerOf(string definition) =>
            parser.WorkflowDefinitions[definition].Roles.Single(r => r.Name == "Reviewer");

        Assert.Single(parser.WorkflowDefinitions["Omit"].AllActions, a => a.Name == "Approve");
        Assert.Equal(1, ReviewerOf("Omit").Actions.Count(a => a.WorkflowDefinition == "Omit" && a.Name == "Approve"));

        Assert.Single(parser.WorkflowDefinitions["Merge"].AllActions, a => a.Name == "Approve");
        Assert.Single(ReviewerOf("Merge").Actions, a => a.WorkflowDefinition == "Merge" && a.Name == "Approve");
        Assert.Equal("Child approval",
            parser.WorkflowDefinitions["Merge"].AllActions.Single(a => a.Name == "Approve").Label!.En);
        Assert.Single(parser.WorkflowDefinitions["Merge"].AllActions, a => a.Name == "Reject");

        Assert.DoesNotContain(parser.WorkflowDefinitions["Clear"].AllActions, a => a.Name == "Approve");
        Assert.DoesNotContain(ReviewerOf("Clear").Actions, a => a.WorkflowDefinition == "Clear" && a.Name == "Approve");
    }

    [Fact]
    public void ApplyInheritance_ChildPageWithSameName_IsNotOverwrittenByParent()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases\nproperties:\n  - name: Foo\n    type: String",
            ["Base/Forms/Edit.yaml"] = "name: Edit\npages:\n  - name: P\n    elements:\n      - question: Foo",
            ["Child/Entity.yaml"] = "name: Child\ntitlePlural: Children\ninheritsFrom: Base",
            ["Child/Forms/Edit.yaml"] = "name: Edit\npages:\n  - name: P\n    elements: []"
        }));

        var childForm = parser.WorkflowDefinitions["Child"].Forms.Get("Edit");

        var page = Assert.Single(childForm.Pages);
        Assert.Empty(page.PageElements);
    }

    [Fact]
    public void ApplyInheritance_ChildPageWithNewName_IsInsertedBeforeIt()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases\nproperties:\n  - name: Foo\n    type: String",
            ["Base/Forms/Edit.yaml"] = "name: Edit\npages:\n  - name: P1\n    elements:\n      - question: Foo",
            ["Merge/Entity.yaml"] = "name: Merge\ntitlePlural: Ms\ninheritsFrom: Base",
            ["Merge/Forms/Edit.yaml"] = "name: Edit\npages:\n  - name: P2\n    elements:\n      - question: Foo"
        }));

        var mergedForm = parser.WorkflowDefinitions["Merge"].Forms.Get("Edit");

        Assert.Equal(["P1", "P2"], mergedForm.Pages.Select(p => p.Name));
    }

    [Fact]
    public void ApplyInheritance_ChildWithExplicitlyEmptyPages_DoesNotInheritParentPages()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases\nproperties:\n  - name: Foo\n    type: String",
            ["Base/Forms/Edit.yaml"] = "name: Edit\npages:\n  - name: ParentPage\n    elements:\n      - question: Foo",
            ["Child/Entity.yaml"] = "name: Child\ntitlePlural: Children\ninheritsFrom: Base",
            ["Child/Forms/Edit.yaml"] = "name: Edit\npages: []"
        }));

        var childForm = parser.WorkflowDefinitions["Child"].Forms.Get("Edit");

        // The parent's "ParentPage" is not inherited because the child explicitly cleared its pages.
        // Since the child form then has zero pages, the default-page fallback kicks in and generates
        // a page containing all properties, rather than reusing the parent's page/elements.
        var page = Assert.Single(childForm.Pages);
        Assert.NotEqual("ParentPage", page.Name);
        Assert.Equal(["Foo"], page.PageElements.Select(e => e.Question));
    }

    [Fact]
    public void ApplyInheritance_ChildWithOmittedPages_InheritsAllParentPages()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases\nproperties:\n  - name: Foo\n    type: String",
            ["Base/Forms/Edit.yaml"] = "name: Edit\npages:\n  - name: ParentPage\n    elements:\n      - question: Foo",
            ["Child/Entity.yaml"] = "name: Child\ntitlePlural: Children\ninheritsFrom: Base",
            ["Child/Forms/Edit.yaml"] = "name: Edit"
        }));

        var childForm = parser.WorkflowDefinitions["Child"].Forms.Get("Edit");

        // The child's Edit.yaml doesn't specify "pages" at all, so it should inherit
        // the parent's page(s) as-is, rather than clearing them or generating a default page.
        var page = Assert.Single(childForm.Pages);
        Assert.Equal("ParentPage", page.Name);
        Assert.Equal(["Foo"], page.PageElements.Select(e => e.Question));
    }

    [Fact]
    public void AssessmentConfiguration_Inherited_ClearedByNull_OrReplaced()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] =
                "name: Base\ntitlePlural: Bases\nassessments:\n  parts:\n    - name: Whole\n      weight: 1",
            ["Omit/Entity.yaml"] = "name: Omit\ntitlePlural: Os\ninheritsFrom: Base",
            ["Nulled/Entity.yaml"] = "name: Nulled\ntitlePlural: Ns\ninheritsFrom: Base\nassessments:",
            ["Replace/Entity.yaml"] =
                "name: Replace\ntitlePlural: Rs\ninheritsFrom: Base\nassessments:\n  parts:\n    - name: Half\n      weight: 1"
        }));

        Assert.Equal("Whole",
            parser.WorkflowDefinitions["Omit"].AssessmentConfiguration!.Parts.Single().Name);
        Assert.Null(parser.WorkflowDefinitions["Nulled"].AssessmentConfiguration);
        Assert.Equal("Half",
            parser.WorkflowDefinitions["Replace"].AssessmentConfiguration!.Parts.Single().Name);
    }

    [Fact]
    public void FormPages_MergeUnlessExplicitlyCleared()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases\nproperties:\n  - name: Foo\n    type: String",
            ["Base/Forms/Edit.yaml"] = "name: Edit\npages:\n  - name: P1\n    elements:\n      - question: Foo",
            ["Merge/Entity.yaml"] = "name: Merge\ntitlePlural: Ms\ninheritsFrom: Base",
            ["Merge/Forms/Edit.yaml"] = "name: Edit\npages:\n  - name: P2\n    elements:\n      - question: Foo",
            ["Reject/Entity.yaml"] = "name: Reject\ntitlePlural: Rs\ninheritsFrom: Base",
            ["Reject/Forms/Edit.yaml"] = "name: Edit\npages: []"
        }));

        var merged = parser.WorkflowDefinitions["Merge"].Forms.Single(f => f.Name == "Edit").Pages.Select(p => p.Name);
        Assert.Equal(["P1", "P2"], merged);

        // Empty forms get a generated page during preprocessing.
        Assert.DoesNotContain("P1",
            parser.WorkflowDefinitions["Reject"].Forms.Single(f => f.Name == "Edit").Pages.Select(p => p.Name));
    }

    [Fact]
    public void InfoCards_ReplaceByNameInParentOrder_AppendNewCards_AndCanBeCleared()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = """
                                   name: Base
                                   titlePlural: Bases
                                   infoCards:
                                     - name: Student
                                       type: User
                                       title: Student
                                       user: Student
                                     - name: Support
                                       type: Links
                                       title: Support
                                       items:
                                         - name: Handbook
                                           type: Link
                                           text: Handbook
                                           url: https://example.com/handbook
                                   """,
            ["Add/Entity.yaml"] = """
                                  name: Add
                                  titlePlural: Adds
                                  inheritsFrom: Base
                                  infoCards:
                                    - name: Support
                                      enabled: false
                                    - name: Notice
                                      type: Text
                                      title: Notice
                                      content: Course information
                                  """,
            ["Clear/Entity.yaml"] = """
                                    name: Clear
                                    titlePlural: Clears
                                    inheritsFrom: Base
                                    infoCards: []
                                    """
        }));

        var cards = parser.WorkflowDefinitions["Add"].InfoCards;
        Assert.Equal(["Student", "Support", "Notice"], cards.Select(card => card.Name));
        Assert.False(cards[1].Enabled);
        Assert.Equal(InfoCardType.Text, cards[2].Type);
        Assert.Empty(parser.WorkflowDefinitions["Clear"].InfoCards);
    }

    [Fact]
    public void Screens_ChildOverrideDoesNotCreateDuplicate()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases",
            ["Base/Screens/Main.yaml"] = "name: Main\ncolumns: []",
            ["Child/Entity.yaml"] = "name: Child\ntitlePlural: Cs\ninheritsFrom: Base",
            ["Child/Screens/Main.yaml"] = "name: Main\ncolumns: []"
        }));

        Assert.Single(parser.WorkflowDefinitions["Child"].Screens, s => s.Name == "Main");
    }

    [Fact]
    public void ThreeLevelInheritance_PreservesExplicitIntermediateOverride()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases\nsteps:\n  - S",
            ["Base/Steps/S.yaml"] = "name: S\ntitle: BaseTitle\nhierarchyMode: Parallel",
            ["Mid/Entity.yaml"] = "name: Mid\ntitlePlural: Ms\ninheritsFrom: Base",
            ["Mid/Steps/S.yaml"] = "name: S\nhierarchyMode: Sequential",
            ["Leaf/Entity.yaml"] = "name: Leaf\ntitlePlural: Ls\ninheritsFrom: Mid"
        }));

        var leafS = Step(parser.WorkflowDefinitions["Leaf"], "S");
        Assert.Equal("BaseTitle", leafS.Title!.En);
        Assert.Equal(StepHierarchyMode.Sequential, leafS.HierarchyMode);
    }

    [Fact]
    public void FormPageElements_AreNotSharedBetweenSiblingDefinitions()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases\nproperties:\n  - name: Foo\n    type: String",
            ["Base/Forms/Edit.yaml"] = "name: Edit\npages:\n  - name: P\n    elements:\n      - question: Foo",

            ["Child1/Entity.yaml"] =
                "name: Child1\ntitlePlural: C1s\ninheritsFrom: Base\nproperties:\n  - name: Foo\n    type: String",
            ["Child2/Entity.yaml"] =
                "name: Child2\ntitlePlural: C2s\ninheritsFrom: Base\nproperties:\n  - name: Foo\n    type: String"
        }));

        PropertyDefinition QuestionDefinitionOf(string definition) =>
            parser.WorkflowDefinitions[definition].Forms.Get("Edit").Pages.Single().PageElements.Single()
                .QuestionDefinition!;

        var baseDefinition = QuestionDefinitionOf("Base");
        var child1Definition = QuestionDefinitionOf("Child1");
        var child2Definition = QuestionDefinitionOf("Child2");

        Assert.Same(parser.WorkflowDefinitions["Base"].Properties.Get("Foo"), baseDefinition);
        Assert.Same(parser.WorkflowDefinitions["Child1"].Properties.Get("Foo"), child1Definition);
        Assert.Same(parser.WorkflowDefinitions["Child2"].Properties.Get("Foo"), child2Definition);

        Assert.NotSame(baseDefinition, child1Definition);
        Assert.NotSame(child1Definition, child2Definition);
    }
}