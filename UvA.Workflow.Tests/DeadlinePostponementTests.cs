using MongoDB.Bson;
using MongoDB.Driver;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using UvA.Workflow.Api.WorkflowInstances;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Journaling;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Tools;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel.Conditions;
using DomainAction = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.Tests;

public class DeadlinePostponementTests
{
    private readonly ModelService _model = new(new ModelParser(new DictionaryProvider(new Dictionary<string, string>
    {
        ["Common/Roles/Registered.yaml"] = "name: Registered",
        ["Common/Roles/Editor.yaml"] = "name: Editor\nactions:\n  - type: Edit",
        ["Common/Roles/Postponer.yaml"] = "name: Postponer\nactions:\n  - type: View\n  - type: PostponeDeadlines",
        ["Common/Roles/Viewer.yaml"] = "name: Viewer\nactions:\n  - type: View",
        ["Test/Entity.yaml"] =
            "name: Test\ntitlePlural: Tests\nsteps: [First, Shared, Second, Calculated, Literal, NonDate]",
        ["Test/Properties.yaml"] = """
                                   properties:
                                     - name: DueDate
                                       type: Date!
                                       text: Submission deadline
                                     - name: OtherDueDate
                                       type: Date
                                     - name: StartDate
                                       type: Date
                                     - name: TextDate
                                       type: String
                                   """,
        ["Test/Steps/First.yaml"] = "name: First\ndeadline:\n  date: DueDate\n  type: Hard",
        ["Test/Steps/Shared.yaml"] = "name: Shared\ndeadline:\n  date: DueDate",
        ["Test/Steps/Second.yaml"] = "name: Second\ndeadline:\n  date: OtherDueDate",
        ["Test/Steps/Calculated.yaml"] = "name: Calculated\ndeadline:\n  date: addWeeks(StartDate, 2)",
        ["Test/Steps/Literal.yaml"] = "name: Literal\ndeadline:\n  date: '=2026-10-01'",
        ["Test/Steps/NonDate.yaml"] = "name: NonDate\ndeadline:\n  date: TextDate"
    })));

    private readonly Mock<IWorkflowInstanceRepository> _repository = new();
    private readonly Mock<IInstanceJournalService> _journal = new();
    private readonly Mock<IUserService> _users = new();
    private readonly List<PropertyChangeEntry> _entries = [];

    private readonly WorkflowInstance _instance = new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        WorkflowDefinition = "Test",
        CurrentStep = "First",
        Events = [],
        Properties = new()
        {
            ["DueDate"] = DateValue(1),
            ["OtherDueDate"] = DateValue(2),
            ["StartDate"] = DateValue(1),
            ["TextDate"] = "2026-10-01"
        }
    };

    private readonly DeadlinePostponementService _service;

    public DeadlinePostponementTests()
    {
        _users.Setup(user => user.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync(["Postponer"]);
        _repository.Setup(repository => repository.TryUpdateFields(It.IsAny<string>(),
                It.IsAny<FilterDefinition<WorkflowInstance>>(), It.IsAny<UpdateDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _journal.Setup(journal => journal.LogPropertyChange(It.IsAny<string>(),
                It.IsAny<PropertyChangeEntry>(), It.IsAny<CancellationToken>()))
            .Callback<string, PropertyChangeEntry, CancellationToken>((_, entry, _) => _entries.Add(entry))
            .ReturnsAsync(false);
        _service = new DeadlinePostponementService(_model,
            new RightsService(_model, _users.Object, _repository.Object), _repository.Object, _journal.Object, null!,
            null!);
    }

    [Theory]
    [InlineData(RoleAction.Execute, null, false)]
    [InlineData(RoleAction.Execute, null, true)]
    [InlineData(RoleAction.Execute, "1m", true)]
    [InlineData(RoleAction.PostponeDeadlines, null, false)]
    [InlineData(RoleAction.PostponeDeadlines, null, true)]
    [InlineData(RoleAction.PostponeDeadlines, "1m", true)]
    [InlineData(RoleAction.Edit, null, true)]
    public void ActionNames_RequiredOnlyForEffects(RoleAction actionType, string? delay, bool hasEffects)
    {
        var files = new Dictionary<string, string>
        {
            ["Common/Roles/Postponer.yaml"] = "name: Postponer",
            ["Test/Entity.yaml"] = $"""
                                    name: Test
                                    titlePlural: Tests
                                    steps: [First]
                                    globalActions:
                                      - type: {actionType}
                                        roles: [Postponer]
                                    """,
            ["Test/Steps/First.yaml"] = "name: First"
        };
        if (hasEffects)
            files["Test/Entity.yaml"] += "\n    onAction:\n      - showConfetti: true";
        if (delay != null)
            files["Test/Entity.yaml"] += $"\n        delay: {delay}";

        if (hasEffects)
        {
            Assert.Contains("require a name", Assert.Throws<Exception>(() =>
                new ModelParser(new DictionaryProvider(files))).Message);
            files["Test/Entity.yaml"] = files["Test/Entity.yaml"].Replace("- type:", "- name: Notify\n    type:");
        }

        var parser = new ModelParser(new DictionaryProvider(files));
        var action = Assert.Single(parser.WorkflowDefinitions["Test"].GlobalActions);
        Assert.Equal(hasEffects ? "Notify" : null, action.Name);
        if (hasEffects)
            Assert.True(Assert.Single(action.OnAction).ShowConfetti);
    }

    [Fact]
    public async Task Discovery_OnlyDirectDateProperties_AreUnique_AndIncludeExpiredHardDeadlines()
    {
        var deadlines = await _service.GetDeadlines(_instance);

        Assert.Equal(["DueDate", "OtherDueDate"], deadlines.Select(deadline => deadline.Property));
        Assert.Equal("Submission deadline", deadlines[0].Title.En);
        Assert.Equal(Day(1), deadlines[0].Date);
    }

    [Fact]
    public async Task Discovery_RequiresPostponePermission_AndExcludesUnsetAndCompletedDeadlines()
    {
        _users.Setup(user => user.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        Assert.Empty(await _service.GetDeadlines(_instance));
        _users.Setup(user => user.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync(["Postponer"]);
        _instance.Properties.Remove("OtherDueDate");
        foreach (var step in _model.WorkflowDefinitions["Test"].AllSteps.Take(2))
            step.Ends = new Condition { Deadline = "=2999-01-01" };
        Assert.Empty(await _service.GetDeadlines(_instance));
    }

    [Fact]
    public async Task Postpone_RechecksPermission_AndGeneralEditingDoesNotGrantIt()
    {
        Assert.Equal(2, (await _service.GetDeadlines(_instance)).Length);
        _users.Setup(user => user.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync(["Editor"]);

        Assert.Empty(await _service.GetDeadlines(_instance));
        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            _service.Postpone(_instance, [new("DueDate", Day(1), Day(3))], "because",
                UnitTestsHelpers.AdminUser, default));

        _repository.Verify(repository => repository.TryUpdateFields(It.IsAny<string>(),
            It.IsAny<FilterDefinition<WorkflowInstance>>(), It.IsAny<UpdateDefinition<WorkflowInstance>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_entries);
    }

    [Theory]
    [InlineData("First", null, true, "DueDate")]
    [InlineData("Shared", null, true, "DueDate")]
    [InlineData("Second", null, true, "OtherDueDate")]
    [InlineData(null, "DueDate", true, "DueDate")]
    [InlineData(null, null, false, null)]
    public async Task Discovery_RespectsActionStepsPropertiesAndConditions(
        string? step, string? property, bool conditionMet, string? expected)
    {
        _model.WorkflowDefinitions["Test"].Roles.Add(new Role
        {
            Name = "Scoped",
            Actions =
            [
                new DomainAction
                {
                    Type = RoleAction.PostponeDeadlines,
                    Steps = step == null ? [] : [step],
                    PropertyDefinition = property,
                    Condition = new Condition { Deadline = conditionMet ? "=2999-01-01" : "=2000-01-01" }
                }
            ]
        });
        _users.Setup(user => user.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync(["Scoped"]);

        Assert.Equal(expected == null ? [] : [expected],
            (await _service.GetDeadlines(_instance)).Select(deadline => deadline.Property));
        var forbiddenProperty = expected == "DueDate" ? "OtherDueDate" : "DueDate";
        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            _service.Postpone(_instance, [new(forbiddenProperty, Day(1), Day(4))], "because",
                UnitTestsHelpers.AdminUser, default));
        Assert.Empty(_entries);
    }

    [Theory]
    [InlineData("Postponer", "Viewer", false)]
    [InlineData("Editor", "Postponer", true)]
    public async Task Discovery_UsesTheImpersonatedRolesPermission(string realRole, string impersonatedRole,
        bool allowed)
    {
        _users.Setup(user => user.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync([realRole]);
        var impersonation = new Mock<IImpersonationContextService>();
        impersonation.Setup(context => context.GetImpersonatedRole(_instance, It.IsAny<CancellationToken>()))
            .ReturnsAsync(impersonatedRole);
        var service = new DeadlinePostponementService(_model,
            new RightsService(_model, _users.Object, _repository.Object, impersonation.Object),
            _repository.Object, _journal.Object, null!, null!);

        Assert.Equal(allowed ? 2 : 0, (await service.GetDeadlines(_instance)).Length);
    }

    [Fact]
    public async Task Postpone_UpdatesDatesAndLogsEveryChangeWithTrimmedReasonAndRealUser()
    {
        await _service.Postpone(_instance,
            [new("DueDate", Day(1), Day(3)), new("OtherDueDate", Day(2), Day(4))],
            "  illness  ", UnitTestsHelpers.AdminUser, default);
        await _service.Postpone(_instance, [new("DueDate", Day(3), Day(5))],
            "more time", UnitTestsHelpers.AdminUser, default);

        Assert.Equal(DateValue(5), _instance.GetProperty("DueDate"));
        Assert.Equal(DateValue(4), _instance.GetProperty("OtherDueDate"));
        Assert.Equal(3, _entries.Count);
        Assert.Equal(DateValue(1), _entries[0].OldValue);
        Assert.Equal("illness", _entries[0].Reason);
        Assert.Equal(UnitTestsHelpers.AdminUser.UserName, _entries[0].ModifiedBy);
        Assert.Equal(DateTimeKind.Utc, _entries[0].Timestamp.Kind);
        Assert.Equal(DateValue(3), _entries[2].OldValue);
        Assert.Equal("more time", _entries[2].Reason);
        var context = _model.CreateContext(_instance);
        Assert.All(_model.WorkflowDefinitions["Test"].AllSteps.Take(2), step =>
            Assert.Equal(Day(5), DateOnly.FromDateTime(step.Deadline!.Evaluate(context)!.Value.LocalDateTime)));
    }

    [Theory]
    [InlineData("DueDate", 1, "because", "InvalidDeadlineDate")]
    [InlineData("DueDate", 0, "because", "InvalidDeadlineDate")]
    [InlineData("DueDate", 3, "   ", "InvalidPostponementReason")]
    [InlineData("StartDate", 3, "because", "InvalidDeadlineProperty")]
    [InlineData("TextDate", 3, "because", "InvalidDeadlineProperty")]
    public async Task Postpone_RejectsInvalidChangesWithoutWrites(string property, int newDay, string reason,
        string code)
    {
        var exception = await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
            _service.Postpone(_instance, [new(property, Day(1), Day(newDay))], reason,
                UnitTestsHelpers.AdminUser, default));

        Assert.Equal(code, exception.Code);
        Assert.Equal(DateValue(1), _instance.GetProperty("DueDate"));
        _repository.Verify(repository => repository.TryUpdateFields(It.IsAny<string>(),
            It.IsAny<FilterDefinition<WorkflowInstance>>(), It.IsAny<UpdateDefinition<WorkflowInstance>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_entries);
    }

    [Theory]
    [InlineData("unset")]
    [InlineData("completed")]
    [InlineData("condition")]
    public async Task UnavailableDeadline_IsExcludedFromListingAndRejectedBeforeAuthorization(string state)
    {
        if (state == "unset")
            _instance.Properties.Remove("DueDate");
        else
            foreach (var step in _model.WorkflowDefinitions["Test"].AllSteps.Take(2))
                if (state == "completed")
                    step.Ends = new Condition { Deadline = "=2999-01-01" };
                else
                    step.Condition = new Condition { Deadline = "=2000-01-01" };
        _users.Setup(user => user.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync(["Editor"]);

        Assert.DoesNotContain(await _service.GetDeadlines(_instance), deadline => deadline.Property == "DueDate");
        var error = await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
            _service.Postpone(_instance, [new("DueDate", Day(1), Day(3))], "because",
                UnitTestsHelpers.AdminUser, default));

        Assert.Equal("InvalidDeadlineProperty", error.Code);
        _repository.Verify(repository => repository.TryUpdateFields(It.IsAny<string>(),
            It.IsAny<FilterDefinition<WorkflowInstance>>(), It.IsAny<UpdateDefinition<WorkflowInstance>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_entries);
    }

    [Fact]
    public async Task Postpone_ValidatesWholeBatchAndPropertyRulesBeforeWriting()
    {
        var property = _model.WorkflowDefinitions["Test"].Properties.Get("OtherDueDate");
        property.Validation = new Condition { Deadline = "=2000-01-01" };
        var exception = await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
            _service.Postpone(_instance, [new("DueDate", Day(1), Day(3)), new("OtherDueDate", Day(2), Day(4))],
                "because", UnitTestsHelpers.AdminUser, default));

        Assert.Equal("InvalidDeadlineDate", exception.Code);
        Assert.Equal(DateValue(1), _instance.GetProperty("DueDate"));
        Assert.Empty(_entries);
    }

    [Fact]
    public async Task Postpone_RejectsStaleDatesAndConcurrentChanges()
    {
        var stale = await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
            _service.Postpone(_instance, [new("DueDate", Day(0), Day(3))], "because",
                UnitTestsHelpers.AdminUser, default));
        Assert.Equal("DeadlineChanged", stale.Code);

        _repository.Setup(repository => repository.TryUpdateFields(It.IsAny<string>(),
            It.IsAny<FilterDefinition<WorkflowInstance>>(), It.IsAny<UpdateDefinition<WorkflowInstance>>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var concurrent = await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
            _service.Postpone(_instance, [new("DueDate", Day(1), Day(3))], "because",
                UnitTestsHelpers.AdminUser, default));
        Assert.Equal("DeadlineChanged", concurrent.Code);
        Assert.Equal(DateValue(1), _instance.GetProperty("DueDate"));
        Assert.Empty(_entries);
    }

    [Fact]
    public void Controller_ExposesPostponeNextToUndoWithoutASeparateDeadlineGet()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(WorkflowInstancesController).Assembly);
        using var provider = services.BuildServiceProvider();
        var actions = provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .ToArray();
        var postpone = Assert.Single(actions, action => action.ActionName == "PostponeDeadlines");
        Assert.Equal(typeof(WorkflowInstancesController), postpone.ControllerTypeInfo.AsType());
        Assert.Equal("WorkflowInstances/{id}/PostponeDeadlines", postpone.AttributeRouteInfo!.Template);
        Assert.DoesNotContain(actions,
            action => action.AttributeRouteInfo?.Template == "WorkflowInstances/{id}/Deadlines");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InstanceResponse_IncludesOnlyTheUsersPostponableDeadlines(bool canPostpone)
    {
        var factory = StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(
            _model, _repository, roles: canPostpone ? ["Postponer"] : ["Editor"]);

        var response = await factory.Create(_instance, default);

        Assert.Equal(canPostpone ? ["DueDate", "OtherDueDate"] : [],
            response.PostponableDeadlines.Select(deadline => deadline.Property));
    }

    private static DateOnly Day(int day) => new DateOnly(2026, 10, 1).AddDays(day - 1);
    private static BsonDateTime DateValue(int day) => new(Day(day).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local));
}