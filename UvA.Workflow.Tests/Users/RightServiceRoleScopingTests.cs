using Moq;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests.Users;

/// <summary>
/// Verifies that RightsService resolves a user's roles/actions against the specific WorkflowDefinition
/// of the instance (or definition name) being checked, rather than falling back to a global role registry.
/// Uses minimal, purpose-built definitions (via DictionaryProvider) instead of the shared Fixtures model,
/// to isolate scoping behavior from unrelated fixture concerns.
/// </summary>
public class RightsServiceRoleScopingTests
{
    private static RightsService Build(Dictionary<string, string> content, string[] currentUserRoles)
    {
        var modelService = new ModelService(new ModelParser(new DictionaryProvider(content)));
        var userService = new Mock<IUserService>();
        var repository = new Mock<IWorkflowInstanceRepository>();

        userService.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentUserRoles);
        userService.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        repository.Setup(r => r.GetAllById(
                It.IsAny<string[]>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        return new RightsService(modelService, userService.Object, repository.Object);
    }

    private static WorkflowInstance CreateInstance(string workflowDefinition) => new()
    {
        Id = "instance-1",
        WorkflowDefinition = workflowDefinition,
        CurrentStep = "Approval",
        Properties = new(),
        Events = new()
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAllowedActions_GrantsActionOnlyForDefinitionThatDeclaresIt(bool specificStep)
    {
        // "Coordinator" exists both in A and B, but only A's Coordinator has a View action.
        var content = new Dictionary<string, string>
        {
            ["A/Entity.yaml"] = "name: A\ntitlePlural: As\nsteps:\n  - Approval",
            ["A/Roles/Coordinator.yaml"] = "name: Coordinator",
            ["A/Steps/Approval.yaml"] =
                "name: Approval\nactions:\n  - name: View\n    type: View\n    roles: [Coordinator]",
            ["B/Entity.yaml"] = "name: B\ntitlePlural: Bs\nsteps:\n  - Approval",
            ["B/Roles/Coordinator.yaml"] = "name: Coordinator",
            ["B/Steps/Approval.yaml"] = "name: Approval\nactions: []"
        };

        var rightsService = Build(content, ["Coordinator"]);

        var actionsForA = specificStep
            ? await rightsService.GetAllowedActionsForStep(CreateInstance("A"), "Approval", RoleAction.View)
            : await rightsService.GetAllowedActions(CreateInstance("A"), RightsEvaluationMode.RealUser,
                RoleAction.View);
        var actionsForB = specificStep
            ? await rightsService.GetAllowedActionsForStep(CreateInstance("B"), "Approval", RoleAction.View)
            : await rightsService.GetAllowedActions(CreateInstance("B"), RightsEvaluationMode.RealUser,
                RoleAction.View);

        Assert.NotEmpty(actionsForA);
        Assert.Empty(actionsForB);
    }

    [Fact]
    public async Task GetAllowedActions_ByDefinitionName_ResolvesRolesFromThatDefinitionOnly()
    {
        var content = new Dictionary<string, string>
        {
            ["A/Entity.yaml"] =
                "name: A\ntitlePlural: As\nglobalActions:\n  - name: Create\n    type: CreateInstance\n    roles: [Coordinator]",
            ["A/Roles/Coordinator.yaml"] = "name: Coordinator",
            ["B/Entity.yaml"] = "name: B\ntitlePlural: Bs",
            ["B/Roles/Coordinator.yaml"] = "name: Coordinator"
        };

        var rightsService = Build(content, ["Coordinator"]);

        var actionsForA = await rightsService.GetAllowedActions("A", RoleAction.CreateInstance);
        var actionsForB = await rightsService.GetAllowedActions("B", RoleAction.CreateInstance);

        Assert.NotEmpty(actionsForA);
        Assert.Empty(actionsForB);
    }
}