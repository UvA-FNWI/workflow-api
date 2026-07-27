using UvA.Workflow.Tests.Helpers;

namespace UvA.Workflow.Tests;

public class WorkflowInheritanceTests
{
    [Fact]
    public void ProjectRmss_InheritsRelatedUsersAndGroupingFromProject()
    {
        var parser = UnitTestsHelpers.CreateModelParser();
        var projectRmss = parser.WorkflowDefinitions["Project-RMSS"];

        Assert.Contains(projectRmss.RelatedUsers, relatedUser => relatedUser.Property == "Supervisor");
        Assert.Contains(projectRmss.RelatedUsers, relatedUser => relatedUser.Property == "Course.Coordinator");
        Assert.Contains(projectRmss.RelatedUserGrouping!.Groups, group => group.Name == "default");
        Assert.Contains(projectRmss.RelatedUserGrouping.Groups, group => group.Name == "support");
    }

    [Fact]
    public void ModelParser_MergesInheritedAndChildRelatedUsersAndGrouping()
    {
        var parser = new ModelParser(new InheritanceContentProvider());
        var child = parser.WorkflowDefinitions["ChildWorkflow"];

        Assert.Equal(["Supervisor", "Coordinator", "Reviewer"],
            child.RelatedUsers.Select(relatedUser => relatedUser.Property).ToArray());
        Assert.Equal(["default", "support", "review"],
            child.RelatedUserGrouping!.Groups.Select(group => group.Name).ToArray());
    }

    [Fact]
    public void ModelParser_MergesOverriddenStepWithParentStep()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = """
                                   name: Base
                                   titlePlural: Bases
                                   steps:
                                     - Review
                                   """,
            ["Base/Steps/Review.yaml"] = """
                                         name: Review
                                         title: Review
                                         hierarchyMode: Parallel
                                         children:
                                           - SupervisorReview
                                           - ExaminerReview
                                         events:
                                           - name: RequestRevision
                                         """,
            ["Base/Steps/SupervisorReview.yaml"] = "name: SupervisorReview",
            ["Base/Steps/ExaminerReview.yaml"] = "name: ExaminerReview",
            ["Child/Entity.yaml"] = """
                                    name: Child
                                    titlePlural: Children
                                    inheritsFrom: Base
                                    """,
            ["Child/Steps/Review.yaml"] = """
                                          name: Review
                                          children:
                                            - SupervisorReview
                                          """
        }));

        var review = parser.WorkflowDefinitions["Child"].AllSteps.Single(step => step.Name == "Review");

        Assert.Equal(["SupervisorReview"], review.Children.Select(child => child.Name).ToArray());
        Assert.Equal(StepHierarchyMode.Parallel, review.HierarchyMode);
        Assert.Equal("Review", review.Title!.En);
        Assert.Contains(review.Events, ev => ev.Name == "RequestRevision");
    }

    [Fact]
    public void ModelParser_RelinksInheritedParentsToOverriddenDescendants()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases\nsteps:\n  - Thesis",
            ["Base/Steps/Thesis.yaml"] = "name: Thesis\nchildren:\n  - Writing",
            ["Base/Steps/Writing.yaml"] = "name: Writing\nchildren:\n  - Review",
            ["Base/Steps/Review.yaml"] = "name: Review\nchildren:\n  - SupervisorReview\n  - ExaminerReview",
            ["Base/Steps/SupervisorReview.yaml"] = "name: SupervisorReview",
            ["Base/Steps/ExaminerReview.yaml"] = "name: ExaminerReview",
            ["Child/Entity.yaml"] = "name: Child\ntitlePlural: Children\ninheritsFrom: Base",
            ["Child/Steps/Review.yaml"] = "name: Review\nchildren:\n  - SupervisorReview"
        }));

        var child = parser.WorkflowDefinitions["Child"];
        var review = child.Steps.Single(s => s.Name == "Thesis")
            .Children.Single(s => s.Name == "Writing")
            .Children.Single(s => s.Name == "Review");

        Assert.Equal(["SupervisorReview"], review.Children.Select(c => c.Name).ToArray());
        Assert.Equal(["SupervisorReview", "ExaminerReview"],
            parser.WorkflowDefinitions["Base"].AllSteps.Single(s => s.Name == "Review")
                .Children.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void ModelParser_OverridingStep_KeepsInheritedActionsInAllActions()
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Common/Roles/Reviewer.yaml"] = "name: Reviewer",
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases\nsteps:\n  - Approval",
            ["Base/Steps/Approval.yaml"] =
                "name: Approval\ntitle: Approval\nactions:\n  - name: Approve\n    type: Execute\n    roles: [Reviewer]",
            ["Child/Entity.yaml"] = "name: Child\ntitlePlural: Children\ninheritsFrom: Base",
            ["Child/Steps/Approval.yaml"] = "name: Approval\ntitle: Approved?"
        }));

        var child = parser.WorkflowDefinitions["Child"];
        Assert.Single(child.AllActions, a => a.Name == "Approve");
        Assert.Equal("Approved?", child.AllSteps.Single(s => s.Name == "Approval").Title!.En);
    }

    [Fact]
    public void ModelParser_UsesRelatedUserPropertyDisplayNameAsDefaultTitle()
    {
        var parser = new ModelParser(new RelatedUserTitleContentProvider());
        var workflow = parser.WorkflowDefinitions["Project"];

        var supervisor = workflow.RelatedUsers.Single(relatedUser => relatedUser.Property == "Supervisor");
        Assert.Equal("Supervisor", supervisor.DisplayTitle.En);
        Assert.Equal("Begeleider", supervisor.DisplayTitle.Nl);

        var coordinator = workflow.RelatedUsers.Single(relatedUser => relatedUser.Property == "Course.Coordinator");
        Assert.Equal("Coordinator", coordinator.DisplayTitle.En);
        Assert.Equal("Coordinator NL", coordinator.DisplayTitle.Nl);

        var reviewer = workflow.RelatedUsers.Single(relatedUser => relatedUser.Property == "Reviewer");
        Assert.Equal("Configured reviewer", reviewer.DisplayTitle.En);
        Assert.Equal("Geconfigureerde beoordelaar", reviewer.DisplayTitle.Nl);

        var missing = workflow.RelatedUsers.Single(relatedUser => relatedUser.Property == "MissingUser");
        Assert.Equal("MissingUser", missing.DisplayTitle.En);
        Assert.Equal("MissingUser", missing.DisplayTitle.Nl);
    }

    private sealed class InheritanceContentProvider : IContentProvider
    {
        public IEnumerable<string> GetFolders(string? directory = null)
            => directory == null ? ["BaseWorkflow", "ChildWorkflow"] : Array.Empty<string>();

        public IEnumerable<string> GetFiles(string directory) => directory switch
        {
            "BaseWorkflow" => ["BaseWorkflow/Entity.yaml"],
            "ChildWorkflow" => ["ChildWorkflow/Entity.yaml"],
            _ => Array.Empty<string>()
        };

        public string GetFile(string file) => file switch
        {
            "BaseWorkflow/Entity.yaml" => """
                                          name: BaseWorkflow
                                          titlePlural: Base workflows
                                          properties:
                                            - name: Supervisor
                                              type: User
                                            - name: Coordinator
                                              type: User
                                          relatedUsers:
                                            - property: Supervisor
                                              group: default
                                            - property: Coordinator
                                              group: support
                                          relatedUserGrouping:
                                            groups:
                                              - name: default
                                              - name: support
                                          """,
            "ChildWorkflow/Entity.yaml" => """
                                           name: ChildWorkflow
                                           titlePlural: Child workflows
                                           inheritsFrom: BaseWorkflow
                                           properties:
                                             - name: Reviewer
                                               type: User
                                           relatedUsers:
                                             - property: Reviewer
                                               group: review
                                           relatedUserGrouping:
                                             groups:
                                               - name: review
                                           """,
            _ => ""
        };
    }

    private sealed class RelatedUserTitleContentProvider : IContentProvider
    {
        public IEnumerable<string> GetFolders(string? directory = null)
            => directory == null ? ["Context", "Project"] : Array.Empty<string>();

        public IEnumerable<string> GetFiles(string directory) => directory switch
        {
            "Context" => ["Context/Entity.yaml"],
            "Project" => ["Project/Entity.yaml"],
            _ => Array.Empty<string>()
        };

        public string GetFile(string file) => file switch
        {
            "Context/Entity.yaml" => """
                                     name: Context
                                     titlePlural: Contexts
                                     properties:
                                       - name: Coordinator
                                         type: User
                                         text:
                                           en: Coordinator
                                           nl: Coordinator NL
                                     """,
            "Project/Entity.yaml" => """
                                     name: Project
                                     titlePlural: Projects
                                     properties:
                                       - name: Course
                                         type: Context!
                                       - name: Supervisor
                                         type: User
                                         text:
                                           en: Supervisor
                                           nl: Begeleider
                                       - name: Reviewer
                                         type: User
                                         text:
                                           en: Reviewer property
                                           nl: Beoordelaar property
                                     relatedUsers:
                                       - property: Supervisor
                                         group: default
                                       - property: Course.Coordinator
                                         group: default
                                       - property: Reviewer
                                         group: default
                                         text:
                                           en: Configured reviewer
                                           nl: Geconfigureerde beoordelaar
                                       - property: MissingUser
                                         group: default
                                     """,
            _ => ""
        };
    }
}