using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.Screens.Dtos;
using UvA.Workflow.Events;
using UvA.Workflow.Jobs;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using DomainAction = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.Tests.Controllers;

public class BulkActionsTests : ControllerTestsBase
{
    private readonly BulkActionsService _service;
    private readonly DomainAction _action;

    public BulkActionsTests()
    {
        MockCurrentUser("Coordinator");
        MockEmptyRelatedInstanceLookups();
        var definition = _modelService.WorkflowDefinitions["Project"];
        definition.Screens.Single(s => s.Name == "Projects").BulkActions =
        [
            new BulkAction
                { Action = "CoordinatorApproved", Label = "Approve", Icon = new Icon { Type = "checkmark-line" } }
        ];
        _action = definition.Roles.Single(r => r.Name == "Coordinator").Actions
            .Single(a => a.Name == "CoordinatorApproved");
        _action.OnAction = [new Effect { Event = "BulkConsequence" }];
        _service = new BulkActionsService(_modelService, _workflowInstanceRepoMock.Object, _rightsService,
            new ActionExecutionService(_effectService, _jobService, _instanceService, _modelService),
            NullLogger<BulkActionsService>.Instance);
    }

    [Fact]
    public async Task Preview_IsReadOnlyAndChecksEachInstanceWithoutLeakingAvailability()
    {
        var allowed = Instance("ApprovalCoordinator");
        var inactive = Instance("Start");
        var otherWorkflow = Instance("Start", "Context");

        var result = await _service.Preview("Project", "Projects", "CoordinatorApproved",
            [allowed.Id, inactive.Id, otherWorkflow.Id, "missing", allowed.Id], _ct);

        Assert.Equal(["Eligible", "Unavailable", "Unavailable", "Unavailable"],
            result.Rows.Select(row => row.Status.ToString()));
        Assert.Empty(allowed.Events);
        Assert.Empty(inactive.Events);
        _eventRepoMock.Verify(r => r.AddOrUpdateEvent(It.IsAny<WorkflowInstance>(), It.IsAny<InstanceEvent>(),
                It.IsAny<User>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<OperationMetadata?>()),
            Times.Never);
    }

    [Fact]
    public async Task Execute_RechecksEligibilityDeduplicatesAndRunsEffectsWithRealActor()
    {
        var allowed = Instance("ApprovalCoordinator");
        var stale = Instance("ApprovalCoordinator");
        await _service.Preview("Project", "Projects", "CoordinatorApproved", [allowed.Id, stale.Id], _ct);
        stale.CurrentStep = "Start";
        _action.OnAction =
            [new Effect { Event = "BulkConsequence" }, new Effect { Event = "LaterConsequence", Delay = "1m" }];

        var result = await _service.Execute("Project", "Projects", "CoordinatorApproved",
            [allowed.Id, stale.Id, allowed.Id], UnitTestsHelpers.AdminUser, _ct);

        Assert.Equal(["Succeeded", "Unavailable"], result.Rows.Select(row => row.Status.ToString()));
        Assert.Contains("CoordinatorApproved", allowed.Events.Keys);
        Assert.Contains("BulkConsequence", allowed.Events.Keys);
        Assert.DoesNotContain("LaterConsequence", allowed.Events.Keys);
        Assert.Empty(stale.Events);
        _eventRepoMock.Verify(r => r.AddOrUpdateEvent(allowed,
            It.Is<InstanceEvent>(e => e.Id == "CoordinatorApproved"), UnitTestsHelpers.AdminUser, _ct,
            It.IsAny<string?>(), It.IsAny<OperationMetadata?>()), Times.Once);
        _jobRepositoryMock.Verify(r => r.Add(It.Is<Job>(j => !j.IsSynchronous && j.InstanceId == allowed.Id &&
                                                             j.CreatedBy == UnitTestsHelpers.AdminUser.Id), _ct),
            Times.Once);
    }

    [Fact]
    public async Task Execute_DeduplicatesCaseVariantsOfTheSameMongoId()
    {
        var instance = Instance("ApprovalCoordinator");
        var alias = instance.Id.ToUpperInvariant();
        _workflowInstanceRepoMock.Setup(r => r.GetById(alias, _ct)).ReturnsAsync(instance);
        _action.Steps = [];

        var result = await _service.Execute("Project", "Projects", "CoordinatorApproved",
            [instance.Id, alias], UnitTestsHelpers.AdminUser, _ct);

        Assert.Equal("Succeeded", Assert.Single(result.Rows).Status.ToString());
        _eventRepoMock.Verify(r => r.AddOrUpdateEvent(instance,
            It.Is<InstanceEvent>(e => e.Id == "CoordinatorApproved"), UnitTestsHelpers.AdminUser, _ct,
            It.IsAny<string?>(), It.IsAny<OperationMetadata?>()), Times.Once);
    }

    [Fact]
    public async Task Execute_ReportsEffectFailuresAndContinuesWithOtherRows()
    {
        var failed = Instance("ApprovalCoordinator");
        var succeeded = Instance("ApprovalCoordinator");
        _eventRepoMock.Setup(r => r.AddOrUpdateEvent(failed,
                It.Is<InstanceEvent>(e => e.Id == "BulkConsequence"), It.IsAny<User>(), _ct,
                It.IsAny<string?>(), It.IsAny<OperationMetadata?>()))
            .ThrowsAsync(new InvalidOperationException("Effect failed"));

        var result = await _service.Execute("Project", "Projects", "CoordinatorApproved",
            [failed.Id, succeeded.Id], UnitTestsHelpers.AdminUser, _ct);

        Assert.Equal(["Failed", "Succeeded"], result.Rows.Select(row => row.Status.ToString()));
        Assert.Contains("BulkConsequence", succeeded.Events.Keys);
    }

    [Fact]
    public async Task Execute_ContinuesAfterUnexpectedRowFailure()
    {
        var succeeded = Instance("ApprovalCoordinator");
        _workflowInstanceRepoMock.Setup(r => r.GetById("broken", _ct))
            .ThrowsAsync(new InvalidOperationException("Cannot load instance"));

        var result = await _service.Execute("Project", "Projects", "CoordinatorApproved",
            ["broken", succeeded.Id], UnitTestsHelpers.AdminUser, _ct);

        Assert.Equal(["Failed", "Succeeded"], result.Rows.Select(row => row.Status.ToString()));
    }

    [Fact]
    public async Task Preview_RejectsExpiredStepOrInsufficientRoles()
    {
        var instance = Instance("ApprovalCoordinator");
        _modelService.WorkflowDefinitions["Project"].AllSteps.Single(s => s.Name == "ApprovalCoordinator").Deadline =
            new Deadline { Date = "=2000-01-01", Type = DeadlineType.Hard };
        var expired = await _service.Preview("Project", "Projects", "CoordinatorApproved", [instance.Id], _ct);
        Assert.Equal("Unavailable", Assert.Single(expired.Rows).Status.ToString());

        MockCurrentUser("Student");
        var forbidden = await _service.Preview("Project", "Projects", "CoordinatorApproved", [instance.Id], _ct);
        Assert.Equal("Unavailable", Assert.Single(forbidden.Rows).Status.ToString());
    }

    [Fact]
    public async Task Execute_DoesNotRequireClientFeedbackEffects()
    {
        var instance = Instance("ApprovalCoordinator");
        _action.OnAction =
        [
            new Effect { Toast = new Toast { Message = "Done" } }, new Effect { ShowConfetti = true },
            new Effect { Redirect = new Redirect { Url = "https://example.com" } },
            new Effect { Event = "BulkConsequence" }
        ];

        var result = await _service.Execute("Project", "Projects", "CoordinatorApproved", [instance.Id],
            UnitTestsHelpers.AdminUser, _ct);

        Assert.Equal("Succeeded", Assert.Single(result.Rows).Status.ToString());
        Assert.Contains("BulkConsequence", instance.Events.Keys);
    }

    [Theory]
    [InlineData("missing", "Projects", "CoordinatorApproved")]
    [InlineData("Project", "missing", "CoordinatorApproved")]
    [InlineData("Project", "Projects", "NotConfigured")]
    public async Task Preview_RejectsUnconfiguredActions(string workflow, string screen, string action)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.Preview(workflow, screen, action, ["any"], _ct));
    }

    [Fact]
    public async Task Endpoints_RejectUnauthenticatedRequestsAndUnknownActions()
    {
        var controller = new BulkActionsController(_service, _userServiceMock.Object);
        _userServiceMock.Setup(s => s.GetCurrentUser(_ct)).ReturnsAsync((User?)null);
        var input = new BulkActionInputDto(["any"]);
        Assert.IsType<UnauthorizedResult>((await controller.Preview("Project", "Projects", "CoordinatorApproved",
            input, _ct)).Result);
        Assert.IsType<UnauthorizedResult>((await controller.Execute("Project", "Projects", "CoordinatorApproved",
            input, _ct)).Result);
        MockCurrentUser("Coordinator");
        Assert.Equal(404, Assert.IsType<ObjectResult>((await controller.Preview("Project", "Projects", "missing",
            input, _ct)).Result).StatusCode);
    }

    private WorkflowInstance Instance(string step, string workflow = "Project")
    {
        var instance = new WorkflowInstanceBuilder().With(workflow, step).Build();
        MockInstance(instance);
        MockEmptyEventLog(instance);
        return instance;
    }
}