using MongoDB.Bson;
using Moq;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests;

public class FormPreviewPermissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkflowFormGrantIsSufficientForPreview(bool grant)
    {
        var model = CreateModel(grant);
        var users = new Mock<IUserService>();
        users.Setup(u => u.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync(["Reviewer"]);
        var repository = Mock.Of<IWorkflowInstanceRepository>();
        var rights = new RightsService(model, users.Object, repository,
            UnitTestsHelpers.CreateStateResolver(model, repository));

        var forms = await rights.GetPreviewableForms(Instance());

        Assert.Equal(grant ? ["Review"] : Array.Empty<string>(), forms.Select(f => f.Name));
        Assert.False(await rights.Can(Instance(), RoleAction.Submit, "Review"));
    }

    [Fact]
    public async Task PreviewOnlyIncludesFormsNamedInTheGrant()
    {
        var model = CreateModel();
        var definition = model.WorkflowDefinitions["Project"];
        definition.Forms.Add(new Form { Name = "Other", WorkflowDefinition = definition });
        var users = new Mock<IUserService>();
        users.Setup(u => u.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync(["Reviewer"]);
        var repository = Mock.Of<IWorkflowInstanceRepository>();
        var rights = new RightsService(model, users.Object, repository,
            UnitTestsHelpers.CreateStateResolver(model, repository));

        Assert.Equal(["Review"], (await rights.GetPreviewableForms(Instance())).Select(f => f.Name));

        definition.GlobalActions.Single().Forms = ["Review", "Other"];
        Assert.Equal(["Review", "Other"], (await rights.GetPreviewableForms(Instance())).Select(f => f.Name));
    }

    [Fact]
    public async Task AFormGrantDoesNotApplyToOtherWorkflows()
    {
        var model = CreateModel();
        var users = new Mock<IUserService>();
        users.Setup(u => u.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync(["Reviewer"]);
        var instance = Instance();
        instance.WorkflowDefinition = "Other";

        var repository = Mock.Of<IWorkflowInstanceRepository>();
        Assert.Empty(await new RightsService(model, users.Object, repository,
                UnitTestsHelpers.CreateStateResolver(model, repository))
            .GetPreviewableForms(instance));
    }

    [Fact]
    public async Task APreviewActionWithoutFormScopeDoesNotGrantEveryForm()
    {
        var model = CreateModel(grantExtra: "    forms: []\n",
            roleExtra: "actions:\n  - type: Preview\n");
        var users = new Mock<IUserService>();
        users.Setup(u => u.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync(["Reviewer"]);

        var repository = Mock.Of<IWorkflowInstanceRepository>();
        Assert.Empty(await new RightsService(model, users.Object, repository,
                UnitTestsHelpers.CreateStateResolver(model, repository))
            .GetPreviewableForms(Instance()));
    }

    [Theory]
    [InlineData("    steps: [Later]\n")]
    [InlineData("    forms: [Missing]\n")]
    public void InvalidPreviewGlobalActionIsRejected(string extra)
        => Assert.ThrowsAny<Exception>(() => CreateModel(grantExtra: extra));

    [Fact]
    public async Task PreviewIsIndependentOfSubmissionAndCurrentStepButHonoursConditions()
    {
        var model = CreateModel(
            grantExtra: "    condition:\n      value:\n        property: Score\n        equal: 5\n");
        var users = new Mock<IUserService>();
        users.Setup(u => u.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync(["Reviewer"]);
        var repository = Mock.Of<IWorkflowInstanceRepository>();
        var rights = new RightsService(model, users.Object, repository,
            UnitTestsHelpers.CreateStateResolver(model, repository));
        var instance = Instance();
        Assert.Empty(await rights.GetPreviewableForms(instance));
        instance.Properties["Score"] = 5;
        instance.RecordEvent("Review");
        Assert.Single(await rights.GetPreviewableForms(instance));
    }

    [Fact]
    public async Task PreviewUsesTheEffectiveImpersonatedRoleRatherThanTheUsersOtherRoles()
    {
        var model = CreateModel();
        model.WorkflowDefinitions["Project"].Roles.Add(new Role
            { Name = "Viewer", Actions = [new() { Type = RoleAction.View }] });
        var users = new Mock<IUserService>();
        users.Setup(u => u.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync(["Reviewer"]);
        var impersonation = new Mock<IImpersonationContextService>();
        impersonation.Setup(i => i.GetImpersonatedRole(It.IsAny<WorkflowInstance>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Viewer");
        var repository = Mock.Of<IWorkflowInstanceRepository>();
        var rights = new RightsService(model, users.Object, repository,
            UnitTestsHelpers.CreateStateResolver(model, repository), impersonation.Object);

        Assert.Empty(await rights.GetPreviewableForms(Instance()));
    }

    [Fact]
    public async Task WorkflowFormGrantWorksForAnInstanceRole()
    {
        var model = CreateModel();
        var definition = model.WorkflowDefinitions["Project"];
        definition.Properties.Add(new UserPropertyDefinition { Name = "Reviewer", Type = "User" });
        var userId = ObjectId.GenerateNewId().ToString();
        var instance = Instance();
        instance.Properties["Reviewer"] = new BsonDocument
            { ["_id"] = ObjectId.Parse(userId), ["UserName"] = "reviewer" };
        var users = new Mock<IUserService>();
        users.Setup(u => u.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        users.Setup(u => u.GetCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = userId });

        var repository = Mock.Of<IWorkflowInstanceRepository>();
        var forms = await new RightsService(model, users.Object, repository,
                UnitTestsHelpers.CreateStateResolver(model, repository))
            .GetPreviewableForms(instance);

        Assert.Equal(["Review"], forms.Select(form => form.Name));
    }

    internal static ModelService CreateModel(bool grant = true,
        string? grantExtra = null, string? roleExtra = null)
        => new(new ModelParser(new DictionaryProvider(new()
        {
            ["Common/Roles/Reviewer.yaml"] = "name: Reviewer\n" +
                                             roleExtra,
            ["Project/Entity.yaml"] = "name: Project\nproperties:\n  - name: Score\n    type: Double\n" +
                                      (grant
                                          ? "globalActions:\n  - type: Preview\n    roles: [Reviewer]\n" +
                                            ((grantExtra ?? "").Contains("forms:") ? "" : "    forms: [Review]\n") +
                                            grantExtra
                                          : ""),
            ["Other/Entity.yaml"] = "name: Other",
            ["Other/Forms/Review.yaml"] = "name: Review\npages: []",
            ["Project/Forms/Review.yaml"] =
                "name: Review\npages:\n  - name: Content\n    elements:\n      - question: Score"
        })));

    internal static WorkflowInstance Instance() => new()
    {
        Id = ObjectId.GenerateNewId().ToString(), WorkflowDefinition = "Project", Properties = [], Events = []
    };
}