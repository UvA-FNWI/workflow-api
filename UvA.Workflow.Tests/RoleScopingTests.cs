namespace UvA.Workflow.Tests;

/// <summary>
/// Verifies that roles (and the actions attached to them) are scoped to the WorkflowDefinition they belong to,
/// rather than being shared globally across all definitions.
/// </summary>
public class RoleScopingTests
{
    [Fact]
    public void Roles_OnEachDefinition_AreClonesNotSharedWithGlobalRoles()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Common/Roles/Reviewer.yaml"] = "name: Reviewer",
            ["A/Entity.yaml"] = "name: A\ntitlePlural: As\nsteps:\n  - Approval",
            ["A/Steps/Approval.yaml"] =
                "name: Approval\nactions:\n  - name: Approve\n    type: Execute\n    roles: [Reviewer]"
        }));

        var globalReviewer = parser.GlobalRoles.Single(r => r.Name == "Reviewer");
        var definitionReviewer = parser.WorkflowDefinitions["A"].Roles.Single(r => r.Name == "Reviewer");

        Assert.NotSame(globalReviewer, definitionReviewer);
        Assert.Single(definitionReviewer.Actions, a => a.Name == "Approve");
        Assert.Empty(globalReviewer.Actions);
    }

    [Fact]
    public void Roles_ActionsAreIsolatedBetweenSiblingDefinitions()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Common/Roles/Reviewer.yaml"] = "name: Reviewer",
            ["A/Entity.yaml"] = "name: A\ntitlePlural: As\nsteps:\n  - Approval",
            ["A/Steps/Approval.yaml"] =
                "name: Approval\nactions:\n  - name: ApproveA\n    type: Execute\n    roles: [Reviewer]",
            ["B/Entity.yaml"] = "name: B\ntitlePlural: Bs\nsteps:\n  - Approval",
            ["B/Steps/Approval.yaml"] =
                "name: Approval\nactions:\n  - name: ApproveB\n    type: Execute\n    roles: [Reviewer]"
        }));

        var reviewerA = parser.WorkflowDefinitions["A"].Roles.Single(r => r.Name == "Reviewer");
        var reviewerB = parser.WorkflowDefinitions["B"].Roles.Single(r => r.Name == "Reviewer");

        Assert.NotSame(reviewerA, reviewerB);

        Assert.Single(reviewerA.Actions, a => a.Name == "ApproveA" && a.WorkflowDefinition == "A");
        Assert.DoesNotContain(reviewerA.Actions, a => a.Name == "ApproveB");

        Assert.Single(reviewerB.Actions, a => a.Name == "ApproveB" && a.WorkflowDefinition == "B");
        Assert.DoesNotContain(reviewerB.Actions, a => a.Name == "ApproveA");
    }

    [Fact]
    public void Roles_DeclaredOnlyWithinADefinition_AreNotVisibleInSiblingDefinitions()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["A/Entity.yaml"] = "name: A\ntitlePlural: As\nsteps:\n  - Approval",
            ["A/Roles/LocalCoordinator.yaml"] = "name: LocalCoordinator",
            ["A/Steps/Approval.yaml"] =
                "name: Approval\nactions:\n  - name: Approve\n    type: Execute\n    roles: [LocalCoordinator]",
            ["B/Entity.yaml"] = "name: B\ntitlePlural: Bs"
        }));

        Assert.Contains(parser.WorkflowDefinitions["A"].Roles, r => r.Name == "LocalCoordinator");
        Assert.DoesNotContain(parser.WorkflowDefinitions["B"].Roles, r => r.Name == "LocalCoordinator");
        Assert.DoesNotContain(parser.GlobalRoles, r => r.Name == "LocalCoordinator");
    }

    [Fact]
    public void Roles_DeclaredLocallyWithSameNameAsAGlobalRole_ThrowsDuringParsing()
    {
        var ex = Assert.Throws<Exception>(() => new ModelParser(new DictionaryProvider(new()
        {
            ["Common/Roles/Reviewer.yaml"] = "name: Reviewer",
            ["A/Entity.yaml"] = "name: A\ntitlePlural: As",
            ["A/Roles/Reviewer.yaml"] = "name: Reviewer"
        })));

        Assert.Contains("Reviewer", ex.Message);
    }

    [Fact]
    public void StepAction_ReferencingUndeclaredRole_ThrowsDuringParsing()
    {
        var ex = Assert.Throws<Exception>(() => new ModelParser(new DictionaryProvider(new()
        {
            ["A/Entity.yaml"] = "name: A\ntitlePlural: As\nsteps:\n  - S",
            ["A/Steps/S.yaml"] =
                "name: S\nactions:\n  - name: Approve\n    type: Execute\n    roles: [Ghost]"
        })));

        Assert.Contains("Ghost", ex.Message);
    }

    [Fact]
    public void GlobalAction_ReferencingUndeclaredRole_ThrowsDuringParsing()
    {
        Assert.Throws<ArgumentException>(() => new ModelParser(new DictionaryProvider(new()
        {
            ["A/Entity.yaml"] =
                "name: A\ntitlePlural: As\nglobalActions:\n  - name: Make\n    type: CreateInstance\n    roles: [Ghost]"
        })));
    }

    [Fact]
    public void Roles_InheritedDefinition_MergesParentActionsIntoOwnCloneWithoutMutatingParent()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Common/Roles/Reviewer.yaml"] = "name: Reviewer",
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases\nsteps:\n  - Approval",
            ["Base/Steps/Approval.yaml"] =
                "name: Approval\nactions:\n  - name: BaseApprove\n    type: Execute\n    roles: [Reviewer]",
            ["Child/Entity.yaml"] = "name: Child\ntitlePlural: Cs\ninheritsFrom: Base",
            ["Child/Steps/Approval.yaml"] =
                "name: Approval\nactions:\n  - name: ChildApprove\n    type: Execute\n    roles: [Reviewer]"
        }));

        var baseReviewer = parser.WorkflowDefinitions["Base"].Roles.Single(r => r.Name == "Reviewer");
        var childReviewer = parser.WorkflowDefinitions["Child"].Roles.Single(r => r.Name == "Reviewer");

        Assert.NotSame(baseReviewer, childReviewer);

        // Child inherits the parent's action (rebound to its own definition) plus its own
        Assert.Equal(2, childReviewer.Actions.Count);
        Assert.Contains(childReviewer.Actions, a => a.Name == "BaseApprove" && a.WorkflowDefinition == "Child");
        Assert.Contains(childReviewer.Actions, a => a.Name == "ChildApprove" && a.WorkflowDefinition == "Child");

        // the base definition's own role/action is left untouched
        Assert.Single(baseReviewer.Actions, a => a.Name == "BaseApprove" && a.WorkflowDefinition == "Base");
        Assert.DoesNotContain(baseReviewer.Actions, a => a.Name == "ChildApprove");
    }

    [Fact]
    public void GlobalRoles_RemainTemplatesOnly_UnaffectedByAnyDefinitionsActions()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Common/Roles/Registered.yaml"] = "name: Registered",
            ["A/Entity.yaml"] =
                "name: A\ntitlePlural: As\nglobalActions:\n  - name: Make\n    type: CreateInstance\n    roles: [Registered]",
            ["B/Entity.yaml"] =
                "name: B\ntitlePlural: Bs\nglobalActions:\n  - name: Remove\n    type: Delete\n    roles: [Registered]"
        }));

        var globalRegistered = parser.GlobalRoles.Single(r => r.Name == "Registered");

        Assert.Empty(globalRegistered.Actions);
        Assert.NotSame(globalRegistered, parser.WorkflowDefinitions["A"].Roles.Single(r => r.Name == "Registered"));
        Assert.NotSame(globalRegistered, parser.WorkflowDefinitions["B"].Roles.Single(r => r.Name == "Registered"));
    }

    [Fact]
    public void Roles_WithInheritFrom_ResolvesAgainstSameDefinitionsRoles_NotGlobalRoles()
    {
        // "Senior" role in each definition inherits from a "Reviewer" role that is declared locally to that
        // definition, not from a global template. InheritFrom must resolve within the owning definition.
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["A/Entity.yaml"] = "name: A\ntitlePlural: As\nsteps:\n  - Approval",
            ["A/Roles/Reviewer.yaml"] = "name: Reviewer",
            ["A/Roles/Senior.yaml"] = "name: Senior\ninheritFrom: [Reviewer]",
            ["A/Steps/Approval.yaml"] =
                "name: Approval\nactions:\n  - name: Approve\n    type: Execute\n    roles: [Reviewer]",
            ["B/Entity.yaml"] = "name: B\ntitlePlural: Bs\nsteps:\n  - Approval",
            ["B/Roles/Reviewer.yaml"] = "name: Reviewer",
            ["B/Roles/Senior.yaml"] = "name: Senior\ninheritFrom: [Reviewer]",
            ["B/Steps/Approval.yaml"] =
                "name: Approval\nactions:\n  - name: Reject\n    type: Execute\n    roles: [Reviewer]"
        }));

        var seniorA = parser.WorkflowDefinitions["A"].Roles.Single(r => r.Name == "Senior");
        var seniorB = parser.WorkflowDefinitions["B"].Roles.Single(r => r.Name == "Senior");

        // Senior in A inherits A's Reviewer actions (Approve), not B's (Reject).
        Assert.Single(seniorA.Actions, a => a.Name == "Approve");
        Assert.DoesNotContain(seniorA.Actions, a => a.Name == "Reject");

        // Senior in B inherits B's Reviewer actions (Reject), not A's (Approve).
        Assert.Single(seniorB.Actions, a => a.Name == "Reject");
        Assert.DoesNotContain(seniorB.Actions, a => a.Name == "Approve");
    }

    [Fact]
    public void Roles_LocallyDeclaredWithSameNameInUnrelatedDefinitions_DoNotShareActionsOrIdentity()
    {
        // Both definitions independently declare their own "Coordinator" role (not global, not related by
        // inheritance). They happen to share a name, but must remain fully independent.
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["A/Entity.yaml"] = "name: A\ntitlePlural: As\nsteps:\n  - Approval",
            ["A/Roles/Coordinator.yaml"] = "name: Coordinator",
            ["A/Steps/Approval.yaml"] =
                "name: Approval\nactions:\n  - name: ApproveA\n    type: Execute\n    roles: [Coordinator]",
            ["B/Entity.yaml"] = "name: B\ntitlePlural: Bs\nsteps:\n  - Approval",
            ["B/Roles/Coordinator.yaml"] = "name: Coordinator",
            ["B/Steps/Approval.yaml"] =
                "name: Approval\nactions:\n  - name: ApproveB\n    type: Execute\n    roles: [Coordinator]"
        }));

        var coordinatorA = parser.WorkflowDefinitions["A"].Roles.Single(r => r.Name == "Coordinator");
        var coordinatorB = parser.WorkflowDefinitions["B"].Roles.Single(r => r.Name == "Coordinator");

        Assert.NotSame(coordinatorA, coordinatorB);
        Assert.Single(coordinatorA.Actions, a => a.Name == "ApproveA");
        Assert.DoesNotContain(coordinatorA.Actions, a => a.Name == "ApproveB");
        Assert.Single(coordinatorB.Actions, a => a.Name == "ApproveB");
        Assert.DoesNotContain(coordinatorB.Actions, a => a.Name == "ApproveA");
    }
}