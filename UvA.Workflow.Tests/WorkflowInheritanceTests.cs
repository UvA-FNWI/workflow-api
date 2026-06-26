namespace UvA.Workflow.Tests;

public class WorkflowInheritanceTests
{
    [Fact]
    public void ProjectRmss_InheritsRelatedUsersAndGroupingFromProject()
    {
        var parser = new ModelParser(new FileSystemProvider("../../../../Examples/Projects"));
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
}