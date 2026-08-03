namespace UvA.Workflow.Tests;

public class PresenceAwareInheritanceTests
{
    private static Step Step(WorkflowDefinition def, string name) => def.AllSteps.Single(s => s.Name == name);

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

        var reviewer = parser.Roles.Single(r => r.Name == "Reviewer");

        Assert.Single(parser.WorkflowDefinitions["Omit"].AllActions, a => a.Name == "Approve");
        Assert.Equal(1, reviewer.Actions.Count(a => a.WorkflowDefinition == "Omit" && a.Name == "Approve"));

        Assert.Single(parser.WorkflowDefinitions["Merge"].AllActions, a => a.Name == "Approve");
        Assert.Single(reviewer.Actions, a => a.WorkflowDefinition == "Merge" && a.Name == "Approve");
        Assert.Equal("Child approval",
            parser.WorkflowDefinitions["Merge"].AllActions.Single(a => a.Name == "Approve").Label!.En);
        Assert.Single(parser.WorkflowDefinitions["Merge"].AllActions, a => a.Name == "Reject");

        Assert.DoesNotContain(parser.WorkflowDefinitions["Clear"].AllActions, a => a.Name == "Approve");
        Assert.DoesNotContain(reviewer.Actions, a => a.WorkflowDefinition == "Clear" && a.Name == "Approve");
    }

    [Fact]
    public void GlobalActions_MergeUnlessClearedAndRemainDiscoverable()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Common/Roles/Registered.yaml"] = "name: Registered",
            ["Base/Entity.yaml"] =
                "name: Base\ntitlePlural: Bases\nglobalActions:\n  - name: Make\n    type: CreateInstance\n    roles: [Registered]",
            ["Omit/Entity.yaml"] = "name: Omit\ntitlePlural: Os\ninheritsFrom: Base",
            ["Merge/Entity.yaml"] =
                "name: Merge\ntitlePlural: Ms\ninheritsFrom: Base\nglobalActions:\n  - name: Remove\n    type: Delete\n    roles: [Registered]",
            ["Clear/Entity.yaml"] = "name: Clear\ntitlePlural: Cs\ninheritsFrom: Base\nglobalActions: []"
        }));

        var registered = parser.Roles.Single(r => r.Name == "Registered");

        Assert.Single(parser.WorkflowDefinitions["Omit"].AllActions, a => a.Name == "Make");
        Assert.Equal(1, registered.Actions.Count(a => a.WorkflowDefinition == "Omit" && a.Name == "Make"));

        Assert.Single(parser.WorkflowDefinitions["Merge"].AllActions, a => a.Name == "Make");
        Assert.Single(parser.WorkflowDefinitions["Merge"].AllActions, a => a.Name == "Remove");
        Assert.Equal(2, registered.Actions.Count(a => a.WorkflowDefinition == "Merge"));

        Assert.DoesNotContain(parser.WorkflowDefinitions["Clear"].AllActions, a => a.Name == "Make");
        Assert.DoesNotContain(registered.Actions, a => a.WorkflowDefinition == "Clear" && a.Name == "Make");
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
            ["Base/Forms/Edit.yaml"] = "name: Edit\npages:\n  - name: P1\n    fields: [Foo]",
            ["Merge/Entity.yaml"] = "name: Merge\ntitlePlural: Ms\ninheritsFrom: Base",
            ["Merge/Forms/Edit.yaml"] = "name: Edit\npages:\n  - name: P2\n    fields: [Foo]",
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
    public void RelatedUserGroups_ExplicitEmptyClearsInheritedGroups()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = """
                                   name: Base
                                   titlePlural: Bases
                                   relatedUserGrouping:
                                     groups:
                                       - name: default
                                   """,
            ["Clear/Entity.yaml"] = """
                                    name: Clear
                                    titlePlural: Clears
                                    inheritsFrom: Base
                                    relatedUserGrouping:
                                      groups: []
                                    """
        }));

        Assert.Empty(parser.WorkflowDefinitions["Clear"].RelatedUserGrouping!.Groups);
    }

    [Fact]
    public void Resources_MergeUnlessExplicitlyCleared()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = """
                                   name: Base
                                   titlePlural: Bases
                                   resources:
                                     - name: Support
                                       title: Support
                                       type: Links
                                       items:
                                         - name: Handbook
                                           type: Link
                                           text: Handbook
                                           url: https://example.com/handbook
                                     - name: Guide
                                       title: Guide
                                       type: Text
                                       content: Study guide
                                   """,
            ["Add/Entity.yaml"] = """
                                  name: Add
                                  titlePlural: Adds
                                  inheritsFrom: Base
                                  resources:
                                    - name: Course
                                      title: Course
                                      type: Text
                                      content: Course information
                                  """,
            ["Clear/Entity.yaml"] = """
                                    name: Clear
                                    titlePlural: Clears
                                    inheritsFrom: Base
                                    resources: []
                                    """
        }));

        Assert.Equal(["Support", "Guide", "Course"],
            parser.WorkflowDefinitions["Add"].Resources.Select(r => r.Name).ToArray());
        Assert.Empty(parser.WorkflowDefinitions["Clear"].Resources);
    }

    [Fact]
    public void ResourceItems_MergeUnlessExplicitlyCleared()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = """
                                   name: Base
                                   titlePlural: Bases
                                   resources:
                                     - name: Support
                                       title: Support
                                       type: Links
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
                                  resources:
                                    - name: Support
                                      title: Support
                                      type: Links
                                      items:
                                        - name: Contact
                                          type: Link
                                          text: Contact
                                          url: https://example.com/contact
                                  """,
            ["Clear/Entity.yaml"] = """
                                    name: Clear
                                    titlePlural: Clears
                                    inheritsFrom: Base
                                    resources:
                                      - name: Support
                                        title: Support
                                        type: Text
                                        content: Contact support
                                        items: []
                                    """
        }));

        Assert.Equal(["Contact", "Handbook"],
            parser.WorkflowDefinitions["Add"].Resources.Single().Items!.Select(i => i.Name).ToArray());
        Assert.Empty(parser.WorkflowDefinitions["Clear"].Resources.Single().Items!);
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
}