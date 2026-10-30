using MongoDB.Bson;
using Moq;
using UvA.Workflow.Events;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Tests.Impersonation;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;
using DomainAction = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.Tests.Users;

public class RightsServiceHistoricalViewTests : ControllerTestsBase
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public async Task HistoricalView_UsesSnapshotConditionsWithoutChangingLiveAccess(
        bool sharedThen, bool sharedNow, bool propertyCondition)
    {
        var view = new DomainAction
        {
            Type = RoleAction.View, Form = "Start",
            Condition = new() { Event = new() { Id = "Start" } }
        };
        if (propertyCondition)
            view.Condition = new() { Value = new() { Property = "Title", Equal = "=Shared" } };
        _modelService.WorkflowDefinitions["Project"].Roles.Add(new Role
        {
            Name = "HistoryViewer", Actions = [view, new() { Type = RoleAction.Edit, Form = "Start" }]
        });
        MockCurrentUser("HistoryViewer");
        var current = ImpersonationTestHelpers.CreateProjectInstance();
        var snapshot = ImpersonationTestHelpers.CreateProjectInstance(current.Id);
        snapshot.Properties["Title"] = sharedThen ? "Shared" : "Hidden";
        current.Properties["Title"] = sharedNow ? "Shared" : "Hidden";
        if (sharedThen)
            snapshot.Events["Start"] = new InstanceEvent { Id = "Start", Date = DateTime.UtcNow.AddDays(-1) };
        if (sharedNow)
            current.Events["Start"] = new InstanceEvent { Id = "Start", Date = DateTime.UtcNow };

        var historical = await _rightsService.GetAllowedHistoricalViewActions(current, [snapshot, current]);
        var live = await _rightsService.GetAllowedActions(current, RoleAction.View);

        Assert.Equal(sharedThen, historical[snapshot].Contains(view));
        Assert.Equal(sharedNow, historical[current].Contains(view));
        Assert.Equal(sharedNow, live.Contains(view));
        Assert.All(historical.Values.SelectMany(actions => actions),
            action => Assert.Equal(RoleAction.View, action.Type));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HistoricalView_UsesSnapshotAvailabilityForStepScopedRules(bool availableThen)
    {
        var view = new DomainAction { Type = RoleAction.View, Form = "Start", Steps = ["Start"] };
        _modelService.WorkflowDefinitions["Project"].Roles.Add(new Role { Name = "HistoryViewer", Actions = [view] });
        MockCurrentUser("HistoryViewer");
        var current = ImpersonationTestHelpers.CreateProjectInstance();
        var snapshot = ImpersonationTestHelpers.CreateProjectInstance(current.Id);
        var completed = availableThen ? current : snapshot;
        completed.Events["Start"] = new InstanceEvent { Id = "Start", Date = DateTime.UtcNow.AddDays(-2) };
        completed.Events["ApproveSubject"] = new InstanceEvent
            { Id = "ApproveSubject", Date = DateTime.UtcNow.AddDays(-1) };
        (availableThen ? snapshot : current).CurrentStep = "Start";

        var historical = (await _rightsService.GetAllowedHistoricalViewActions(current, [snapshot]))[snapshot];
        var live = await _rightsService.GetAllowedActions(current, RoleAction.View);

        Assert.Equal(availableThen, historical.Contains(view));
        Assert.Equal(!availableThen, live.Contains(view));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task HistoricalView_UsesCurrentDirectRoleAssignments(bool assignedNow, bool assignedThen)
    {
        MockCurrentUser();
        var current = ImpersonationTestHelpers.CreateProjectInstance();
        var snapshot = ImpersonationTestHelpers.CreateProjectInstance(current.Id);
        var user = InstanceUser.FromUser(UnitTestsHelpers.AdminUser).ToBsonDocument();
        if (assignedNow)
            current.Properties["Supervisor"] = user;
        if (assignedThen)
            snapshot.Properties["Supervisor"] = user;

        var actions = (await _rightsService.GetAllowedHistoricalViewActions(current, [snapshot]))[snapshot];

        Assert.Equal(assignedNow, actions.Any(action => action.MatchesForm("AssessmentSupervisor")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HistoricalView_UsesTheCurrentReferenceForInheritedRoles(bool assignedNow)
    {
        MockCurrentUser();
        var current = ImpersonationTestHelpers.CreateProjectInstance();
        var snapshot = ImpersonationTestHelpers.CreateProjectInstance(current.Id);
        var currentCourse = ObjectId.GenerateNewId().ToString();
        var oldCourse = ObjectId.GenerateNewId().ToString();
        current.Properties["Course"] = currentCourse;
        snapshot.Properties["Course"] = oldCourse;
        var user = InstanceUser.FromUser(UnitTestsHelpers.AdminUser).ToBsonDocument();
        var courses = new[]
        {
            new BsonDocument
                { ["_id"] = currentCourse, ["Coordinator"] = assignedNow ? new BsonArray { user } : new BsonArray() },
            new BsonDocument
                { ["_id"] = oldCourse, ["Coordinator"] = assignedNow ? new BsonArray() : new BsonArray { user } }
        };
        _workflowInstanceRepoMock.Setup(repository => repository.GetAllById(
                It.IsAny<string[]>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string[] ids, Dictionary<string, string> _, CancellationToken _) =>
                courses.Where(course => ids.Contains(course["_id"].AsString))
                    .Select(course => course.ToDictionary(element => element.Name, element => element.Value)).ToList());

        var actions = (await _rightsService.GetAllowedHistoricalViewActions(current, [snapshot]))[snapshot];

        Assert.Equal(assignedNow, actions.Any(action => action.MatchesForm("AssessmentReviewer")));
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Supervisor")]
    [InlineData("MissingRole")]
    public async Task HistoricalView_UsesCurrentImpersonationWithoutAddingRealRoles(string role)
    {
        MockCurrentUser("Coordinator");
        var current = ImpersonationTestHelpers.CreateProjectInstance();
        var snapshot = ImpersonationTestHelpers.CreateProjectInstance(current.Id);
        var impersonation = new Mock<IImpersonationContextService>();
        impersonation.Setup(context => context.GetImpersonatedRole(
                It.IsAny<WorkflowInstance>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowInstance instance, CancellationToken _) =>
                ReferenceEquals(instance, current) ? role : null);
        var rights = new RightsService(_modelService, _userServiceMock.Object, _workflowInstanceRepoMock.Object,
            impersonation.Object);

        var actions = (await rights.GetAllowedHistoricalViewActions(current, [snapshot]))[snapshot];

        Assert.DoesNotContain(actions, action => action.MatchesForm("AssessmentReviewer"));
        Assert.Equal(role == "Supervisor", actions.Any(action => action.MatchesForm("AssessmentSupervisor")));
        if (role == "MissingRole")
            Assert.Empty(actions);
    }
}