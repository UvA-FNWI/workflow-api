using Moq;
using MongoDB.Bson;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests.Users;

public class HiddenPropertyRightsTests : ControllerTestsBase
{
    [Theory]
    [InlineData("Student", false)]
    [InlineData("Coordinator", true)]
    [InlineData("Unknown", false)]
    public async Task HistoricalActions_UseCurrentRolePreviewAndSnapshotConditions(string role, bool visible)
    {
        var current = new WorkflowInstanceBuilder().With("Project", "Start")
            .WithProperties(("Title", _ => "Private")).Build();
        var snapshot = new WorkflowInstanceBuilder().With("Project", "Start", id: current.Id)
            .WithProperties(("Title", _ => "Released")).Build();
        var definition = _modelService.WorkflowDefinitions["Project"];
        definition.Properties.Single(p => p.Name == "Title").Visibility = PropertyVisibility.Hidden;
        definition.Roles.Single(r => r.Name == "Coordinator").Actions.Add(new()
        {
            Type = RoleAction.ViewHidden, Form = "Start", PropertyDefinition = "Title",
            Condition = new() { Value = new() { Property = "Title", Equal = "=Released" } }
        });
        MockCurrentUser("Coordinator");
        MockEmptyRelatedInstanceLookups();
        var impersonation = new Mock<IImpersonationContextService>();
        impersonation.Setup(s => s.GetImpersonatedRole(It.IsAny<WorkflowInstance>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowInstance instance, CancellationToken _) =>
                ReferenceEquals(instance, current) ? role : null);
        var rights = new RightsService(_modelService, _userServiceMock.Object, _workflowInstanceRepoMock.Object,
            impersonation.Object);

        var actions =
            await rights.GetAllowedHistoricalActions(current, snapshot, RoleAction.View, RoleAction.ViewHidden);
        var status = _modelService.GetQuestionStatus(snapshot, _modelService.GetForm(snapshot, "Start"), actions);

        Assert.Equal(visible, status["Title"].IsVisible);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HistoricalActions_InheritRolesFromTheCurrentReference(bool assignedNow)
    {
        var current = new WorkflowInstanceBuilder().With("Project", "Start")
            .WithProperties(("Course", _ => "current-course")).Build();
        var snapshot = new WorkflowInstanceBuilder().With("Project", "Start", id: current.Id)
            .WithProperties(("Course", _ => "old-course")).Build();
        var definition = _modelService.WorkflowDefinitions["Project"];
        definition.Properties.Single(p => p.Name == "Title").Visibility = PropertyVisibility.Hidden;
        definition.Roles.Single(r => r.Name == "Coordinator").Actions.Add(new()
        {
            Type = RoleAction.ViewHidden, Form = "Start", PropertyDefinition = "Title"
        });
        MockCurrentUser();
        var user = InstanceUser.FromUser(UnitTestsHelpers.AdminUser).ToBsonDocument();
        _workflowInstanceRepoMock.Setup(r => r.GetAllById(It.IsAny<string[]>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string[] ids, Dictionary<string, string> _, CancellationToken _) => ids.Select(id =>
                new Dictionary<string, BsonValue>
                {
                    ["_id"] = id,
                    ["Coordinator"] = (id == "current-course") == assignedNow ? new BsonArray { user } : new BsonArray()
                }).ToList());

        var actions = await _rightsService.GetAllowedHistoricalActions(current, snapshot, RoleAction.ViewHidden);
        var status = _modelService.GetQuestionStatus(snapshot, _modelService.GetForm(snapshot, "Start"), actions);

        Assert.Equal(assignedNow, status["Title"].IsVisible);
    }

    [Theory]
    [InlineData(null, true, true)]
    [InlineData(null, false, false)]
    [InlineData("Student", true, false)]
    [InlineData("Coordinator", true, true)]
    [InlineData("Unknown", true, false)]
    public async Task QuestionStatus_AppliesGrantConditionsAndTheImpersonatedRole(
        string? impersonatedRole, bool released, bool visible)
    {
        var definition = _modelService.WorkflowDefinitions["Project"];
        definition.Properties.Single(p => p.Name == "Title").Visibility = PropertyVisibility.Hidden;
        definition.Properties.Single(p => p.Name == "Subject").Visibility = PropertyVisibility.Hidden;
        definition.Roles.Single(r => r.Name == "Coordinator").Actions.Add(new()
        {
            Type = RoleAction.ViewHidden, Form = "Start", PropertyDefinition = "Title",
            Condition = new Condition { Event = new EventCondition { Id = "Released" } }
        });
        var builder = new WorkflowInstanceBuilder().With("Project", "Start");
        if (released) builder.WithEvent("Released", DateTime.UtcNow);
        var instance = builder.Build();
        MockCurrentUser("Coordinator");
        MockEmptyRelatedInstanceLookups();
        var impersonation = new Mock<IImpersonationContextService>();
        impersonation.Setup(s => s.GetImpersonatedRole(instance, It.IsAny<CancellationToken>()))
            .ReturnsAsync(impersonatedRole);
        var rights = new RightsService(_modelService, _userServiceMock.Object, _workflowInstanceRepoMock.Object,
            impersonation.Object);

        var status = await rights.GetQuestionStatus(instance, _modelService.GetForm(instance, "Start"));

        Assert.Equal(visible, status["Title"].IsVisible);
        Assert.False(status["Subject"].IsVisible);
        Assert.True(status["EC"].IsVisible);
    }
}