using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Moq;
using UvA.Workflow.Api.Actions;
using UvA.Workflow.Api.Actions.Dtos;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Deadlines;
using UvA.Workflow.Jobs;
using UvA.Workflow.Journaling;
using UvA.Workflow.Notifications;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.WorkflowInstances;
using Action = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.Tests;

public class PostponeDeadlineTests : ControllerTestsBase
{
    private readonly WorkflowInstance _instance;
    private readonly Action _action;
    private readonly Form _form;
    private readonly PostponeDeadlineService _service;
    private readonly ActionsController _controller;
    private static readonly DateTimeOffset Original = DateTimeOffset.Parse("2000-01-01T12:00:00+01:00");
    private readonly List<PropertyChangeEntry> _changes = [];

    public PostponeDeadlineTests()
    {
        var definition = _modelService.WorkflowDefinitions["Project"];
        foreach (var step in definition.AllSteps) step.Deadline = null;
        definition.AllSteps.Single(step => step.Name == "Start").Deadline =
            new Deadline { Date = "Deadline", Type = DeadlineType.Hard };
        definition.AllSteps.Single(step => step.Name == "Upload").Deadline = "EndDate";
        definition.AllSteps.Single(step => step.Name == "Assessment").Deadline = "=2000-01-01";
        definition.AllSteps.Single(step => step.Name == "Publication").Deadline = "addDays(Deadline, 1)";
        definition.ValueSets.Add(new ValueSet
        {
            Name = "PostponementReason", Values =
            [
                new Choice { Name = "Research", Text = new("Research delay", "Vertraging onderzoek") },
                new Choice { Name = "Other", Text = new("Other", "Overig") }
            ]
        });
        var reason = new PropertyDefinition
        {
            Name = "Reason", Type = "PostponementReason!", ParentType = definition,
            Values = definition.ValueSets.Single(set => set.Name == "PostponementReason").Values
        };
        var explanation = new PropertyDefinition { Name = "Explanation", Type = "String", ParentType = definition };
        definition.Properties.AddRange([reason, explanation]);
        _form = new Form
        {
            Name = "ExtensionDialog", Layout = FormLayout.Modal,
            Title = new("Extend deadlines", "Uitstel verlenen"), WorkflowDefinition = definition,
            Pages =
            [
                new Page
                {
                    Name = "Reason",
                    PageElements =
                    [
                        new PageElement { Question = reason.Name, QuestionDefinition = reason },
                        new PageElement { Question = explanation.Name, QuestionDefinition = explanation }
                    ]
                }
            ]
        };
        definition.Forms.Add(_form);
        _action = new Action
        {
            Name = "GrantExtension", Type = RoleAction.PostponeDeadlines, Form = _form.Name,
            WorkflowDefinition = definition.Name,
            Roles = ["Coordinator", "Supervisor"],
            OnAction = [new Effect { SendMail = new() { TemplateKey = "DeadlinesPostponed" } }]
        };
        definition.GlobalActions.Add(_action);
        foreach (var role in _action.Roles) definition.Roles.Single(r => r.Name == role).Actions.Add(_action);
        definition.Emails.Add(new TemplateMessage
        {
            Name = "DeadlinesPostponed", To = "student@example.org", Subject = "Extended",
            Body = "Deadline: {{ Deadline }}"
        });
        _instance = new WorkflowInstance
        {
            Id = "507f1f77bcf86cd799439011", WorkflowDefinition = "Project", CurrentStep = "Start",
            Properties = new()
            {
                ["Deadline"] = new BsonDateTime(Original.UtcDateTime),
                ["EndDate"] = new BsonDateTime(Original.AddDays(10).UtcDateTime)
            },
            Events = new()
        };
        MockInstance(_instance);
        MockEmptyEventLog(_instance);
        MockEmptyRelatedInstanceLookups();
        MockCurrentUser("Coordinator");
        _instanceJournalServiceMock.Setup(service =>
                service.LogPropertyChange(_instance.Id, It.IsAny<PropertyChangeEntry>(), It.IsAny<CancellationToken>()))
            .Callback<string, PropertyChangeEntry, CancellationToken>((_, entry, _) => _changes.Add(entry))
            .ReturnsAsync(false);
        _instanceJournalServiceMock.Setup(service =>
                service.GetInstanceJournal(_instance.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new InstanceJournalEntry { PropertyChanges = _changes.ToArray() });
        _service = new(_modelService, _instanceService, _instanceJournalServiceMock.Object, _jobService);
        _controller = new(_workflowInstanceRepoMock.Object, _userServiceMock.Object, _rightsService,
            Factory(), new FormDtoFactory(_modelService, _instanceService),
            [
                new ExecuteActionHandler(_rightsService, _effectService, _jobService, _instanceService),
                new PostponeDeadlinesActionHandler(_rightsService, _service)
            ]);
    }

    private WorkflowInstanceDtoFactory Factory(string[]? roles = null) =>
        StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(
            _modelService, _workflowInstanceRepoMock, roles ?? ["Coordinator"],
            new(new InstanceJournalEntry { PropertyChanges = _changes.ToArray() }, []));

    private PostponeDeadlinesRequest Request(string reason = "Research delay") =>
        new([new("Deadline", Original, new DateOnly(2999, 1, 1))], reason);

    private Task<ActionResult<ExecuteActionPayloadDto>> PostponeAction(
        PostponeDeadlinesRequest request, CancellationToken ct = default) =>
        _controller.ExecuteAction(
            new ExecuteActionInputDto(
                ActionType.PostponeDeadlines,
                _instance.Id,
                _action.Name,
                Input: JsonSerializer.SerializeToElement(request, JsonSerializerOptions.Web)),
            ct == default ? _ct : ct);

    private Task<PostponeDeadlinesResult> Postpone(PostponeDeadlinesRequest request) =>
        _service.Postpone(_instance, _action, request, UnitTestsHelpers.AdminUser, _ct);

    [Theory]
    [InlineData("Coordinator", true)]
    [InlineData("Supervisor", true)]
    [InlineData("Student", false)]
    public async Task ButtonAndEndpointUseConfiguredGlobalActionRoles(string role, bool allowed)
    {
        MockCurrentUser(role);
        var dto = await Factory([role]).Create(_instance, _ct);
        Assert.Equal(allowed, dto.Actions.Any(action => action.Name == _action.Name));
        if (allowed)
        {
            var action = Assert.Single(dto.Actions, action => action.Name == _action.Name);
            Assert.Equal(_action.Name, action.Title.En);
            Assert.Equal(_form.Name, action.Form);
            Assert.Equal(FormLayout.Modal, action.FormLayout);
            var loaded = await _controller.GetForm(_instance.Id, _action.Name!, _ct);
            Assert.Equal(2,
                Assert.IsType<FormDto>(Assert.IsType<OkObjectResult>(loaded.Result).Value).Pages[0].Elements[0]
                    .Question!.Choices!.Length);
        }

        if (!allowed)
        {
            var loaded = await _controller.GetForm(_instance.Id, _action.Name!, _ct);
            Assert.Equal(403, Assert.IsType<ObjectResult>(loaded.Result).StatusCode);
        }

        var response = await PostponeAction(Request(), _ct);
        if (allowed)
        {
            Assert.IsType<OkObjectResult>(response.Result);
        }
        else
        {
            Assert.Equal(403, Assert.IsType<ObjectResult>(response.Result).StatusCode);
            Assert.Empty(_changes);
        }
    }

    [Fact]
    public async Task LoadedFormRetainsChoiceMetadataAndSavesOnlyTheReason()
    {
        _form.Pages[0].PageElements[0].QuestionDefinition!.Values![0].Condition =
            new() { Event = new() { Id = "AllowResearchReason" } };
        var loaded = await _controller.GetForm(_instance.Id, _action.Name!, _ct);
        var form = Assert.IsType<FormDto>(Assert.IsType<OkObjectResult>(loaded.Result).Value);
        Assert.Equal(2, form.Pages[0].Elements[0].Question!.Choices!.Length);
        Assert.Empty((await Postpone(Request("Research"))).Errors);
        Assert.Equal("Research", Assert.Single(_changes).Reason);
        Assert.False(_instance.Properties.ContainsKey("Reason"));
        Assert.False(_instance.Properties.ContainsKey("Explanation"));
    }

    [Fact]
    public async Task StepActionCanLoadFormAndPostponeWhileItsStepIsAvailable()
    {
        _action.Steps = ["Start"];
        DeadlineConfiguration().Type = DeadlineType.Soft;

        var form = await _controller.GetForm(_instance.Id, _action.Name!, _ct);
        Assert.IsType<OkObjectResult>(form.Result);

        var response = await PostponeAction(Request(), _ct);
        Assert.IsType<OkObjectResult>(response.Result);
        Assert.Single(_changes);
    }

    [Fact]
    public async Task StepActionCannotPostponeAfterItsHardDeadline()
    {
        _action.Steps = ["Start"];

        var response = await PostponeAction(Request(), _ct);

        Assert.Equal(403, Assert.IsType<ObjectResult>(response.Result).StatusCode);
        Assert.Empty(_changes);
    }

    [Theory]
    [InlineData("Assessment", "2999-01-01")]
    [InlineData("Publication", "2999-01-01")]
    [InlineData("Unknown", "2999-01-01")]
    [InlineData("Deadline", "1999-01-01")]
    [InlineData("Deadline", "2000-01-01")]
    public async Task InvalidChangesDoNotWriteOrSendMail(string property, string date)
    {
        var result = await Postpone(Request() with { Changes = [new(property, Original, DateOnly.Parse(date))] });
        Assert.Equal(PostponementError.InvalidChanges, Assert.Single(result.Errors));
        Assert.Equal(Original.UtcDateTime, _instance.Properties["Deadline"].ToUniversalTime());
        Assert.Empty(_changes);
        _mailServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RejectsStaleDuplicateEmptyOrPartlyInvalidRequestsBeforeAnyWrites()
    {
        var change = Request().Changes[0];
        foreach (var changes in new DeadlineChange[][]
                 {
                     null!, [], [null!], [change with { Property = null! }], [change, change],
                     [change with { PreviousDate = Original.AddDays(-1) }],
                     [change, change with { Property = "Unknown" }]
                 })
            Assert.Equal(PostponementError.InvalidChanges,
                Assert.Single((await Postpone(Request() with { Changes = changes })).Errors));
        Assert.Empty(_changes);
        _mailServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RepeatedExtensionsReopenExpiredRightsAndLogAndEmailEachTime()
    {
        MockCurrentUser("Student");
        Assert.False(await _rightsService.Can(_instance, RoleAction.Submit, "Start"));
        MockCurrentUser("Coordinator");
        for (var year = 2998; year <= 2999; year++)
        {
            var previous = new DateTimeOffset(_instance.Properties["Deadline"].ToUniversalTime());
            Assert.Empty((await Postpone(Request() with
            {
                Changes = [new("Deadline", previous, new DateOnly(year, 1, 1))]
            })).Errors);
        }

        Assert.Equal(2, _changes.Count);
        Assert.All(_changes, entry => Assert.Equal(UnitTestsHelpers.AdminUser.UserName, entry.ModifiedBy));
        _mailServiceMock.Verify(service => service.Send(It.IsAny<MailMessage>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _jobRepositoryMock.Verify(
            repo => repo.Add(It.Is<Job>(job => job.SourceType == JobSource.Action && job.SourceName == _action.Name),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.False(_instance.Events.ContainsKey(_form.Name));
        MockCurrentUser("Student");
        Assert.True(await _rightsService.Can(_instance, RoleAction.Submit, "Start"));
    }

    [Fact]
    public async Task StepDtoIncludesOnlyExtendablePropertyReferencesAndSavedChangeReason()
    {
        Assert.Empty((await Postpone(Request("Other\nResearch delay"))).Errors);
        // Changing unrelated instance properties does not alter the saved reason.
        _instance.Properties["PostponementReasons"] = "Changed later";
        var dto = await Factory().Create(_instance, _ct);
        var step = dto.Steps.SelectMany(step => new[] { step }.Concat(step.Children ?? []))
            .Single(step => step.Id == "Start");
        Assert.Equal("Deadline", step.Deadline!.Property);
        Assert.Equal(Original, step.Deadline.PreviousDate);
        Assert.Equal("Other\nResearch delay", step.Deadline.ChangeReason);
        var definition = _modelService.WorkflowDefinitions["Project"];
        Assert.Null(
            PostponeDeadlineService.GetDeadlineProperty(definition.AllSteps.Single(step => step.Name == "Assessment"),
                definition));
        Assert.Null(
            PostponeDeadlineService.GetDeadlineProperty(definition.AllSteps.Single(step => step.Name == "Publication"),
                definition));
    }

    [Fact]
    public async Task FormReadResolvesTextAndCalloutWithoutWrites()
    {
        _instance.Properties["Course"] = "course-id";
        _form.Pages[0].PageElements =
        [
            new PageElement { Text = new("Extend for {{ Course.Name }}.", "Uitstel voor {{ Course.Name }}.") },
            new PageElement
            {
                Callout = new Callout
                {
                    Title = new("Check {{ Course.Name }}", "Controleer {{ Course.Name }}"),
                    Text = new("Contact the board.", "Neem contact op met de commissie.")
                }
            },
            .. _form.Pages[0].PageElements
        ];
        _workflowInstanceRepoMock.Setup(repo => repo.GetAllById(It.Is<string[]>(ids => ids.Contains("course-id")),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Dictionary<string, BsonValue> { ["_id"] = "course-id", ["Name"] = "Research Methods" }]);
        var previous = _instance.Properties.ToBsonDocument();
        var response = await _controller.GetForm(_instance.Id, _action.Name!, _ct);
        var form = Assert.IsType<FormDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal("Extend for Research Methods.", form.Pages[0].Elements[0].Text!.En);
        Assert.Equal("Uitstel voor Research Methods.", form.Pages[0].Elements[0].Text!.Nl);
        Assert.Equal("Check Research Methods", form.Pages[0].Elements[1].Callout!.Title!.En);
        Assert.Equal(previous, _instance.Properties.ToBsonDocument());
        Assert.Empty(_instance.Events);
        Assert.Empty(_changes);
        _mailServiceMock.VerifyNoOtherCalls();
        _jobRepositoryMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("unconfigured", 403)]
    [InlineData("execute", 200)]
    public async Task FormReadRequiresAnAvailableMatchingAction(string scenario, int status)
    {
        switch (scenario)
        {
            case "unconfigured": _action.Form = null; break;
            case "execute": _action.Type = RoleAction.Execute; break;
        }

        var response = await _controller.GetForm(_instance.Id, "GrantExtension", _ct);
        if (status == 200)
            Assert.IsType<OkObjectResult>(response.Result);
        else
            Assert.Equal(status, Assert.IsType<ObjectResult>(response.Result).StatusCode);
    }

    private Deadline DeadlineConfiguration(string property = "Deadline")
    {
        var definition = _modelService.WorkflowDefinitions["Project"];
        return definition.AllSteps.Single(step =>
            PostponeDeadlineService.GetDeadlineProperty(step, definition)?.Name == property).Deadline!;
    }

    private Task<PostponeDeadlinesResult> Extend(string date, string property = "Deadline") =>
        Postpone(Request() with
        {
            Changes =
            [
                new(property,
                    new DateTimeOffset(_instance.Properties[property].ToUniversalTime()), DateOnly.Parse(date))
            ]
        });

    [Fact]
    public async Task EachDeadlineUsesItsEarliestDatedLogEvenWhenHistoryIsOutOfOrder()
    {
        DeadlineConfiguration().MaxPostponementDays = 14;
        DeadlineConfiguration("EndDate").MaxPostponementDays = 21;
        _instance.Properties["Deadline"] = new BsonDateTime(Original.AddDays(7).UtcDateTime);
        _instance.Properties["EndDate"] = new BsonDateTime(Original.AddDays(17).UtcDateTime);
        _changes.AddRange([
            LoggedDate("Deadline", Original.AddDays(7), 3),
            LoggedDate("EndDate", Original.AddDays(17), 4),
            LoggedDate("EndDate", Original.AddDays(10), 2),
            LoggedDate("Deadline", Original, 1),
            PropertyChangeEntry.Create("Deadline", BsonNull.Value, UnitTestsHelpers.AdminUser)
        ]);

        var dto = await Factory().Create(_instance, _ct);
        var steps = dto.Steps.SelectMany(step => new[] { step }.Concat(step.Children ?? [])).ToArray();
        Assert.Equal(new DateOnly(2000, 1, 15), steps.Single(step => step.Id == "Start").Deadline!.MaxDate);
        Assert.Equal(new DateOnly(2000, 2, 1), steps.Single(step => step.Id == "Upload").Deadline!.MaxDate);
        var response = await PostponeAction(Request() with
        {
            Changes =
            [
                new("Deadline", Original.AddDays(7), new DateOnly(2000, 1, 16)),
                new("EndDate", Original.AddDays(17), new DateOnly(2000, 1, 25))
            ]
        }, _ct);
        var error = Assert.IsType<UnprocessableEntityObjectResult>(response.Result);
        Assert.Equal(PostponementError.MaximumExtensionExceeded,
            Assert.Single(Assert.IsType<PostponementError[]>(error.Value)));
        Assert.Equal(5, _changes.Count);
        Assert.Equal(Original.AddDays(17).UtcDateTime, _instance.Properties["EndDate"].ToUniversalTime());
    }

    [Fact]
    public void FirstChronologicalValueWinsEvenIfALaterEditMovedTheDeadlineBackward()
    {
        DeadlineConfiguration().MaxPostponementDays = 14;
        var journal = new InstanceJournalEntry
        {
            PropertyChanges =
            [
                LoggedDate("Deadline", Original, 2),
                LoggedDate("Deadline", Original.AddDays(7), 1)
            ]
        };
        Assert.Equal(new DateOnly(2000, 1, 22), PostponeDeadlineService.GetMaximumDate(
            DeadlineConfiguration().MaxPostponementDays, Original.AddDays(10), journal.PropertyChanges));
    }

    private static PropertyChangeEntry LoggedDate(string property, DateTimeOffset date, int sequence)
    {
        var entry = PropertyChangeEntry.Create(property, new BsonDateTime(date.UtcDateTime), UnitTestsHelpers.AdminUser)
            .ToBsonDocument();
        entry["Timestamp"] = new BsonDateTime(new DateTime(2026, 1, sequence, 0, 0, 0, DateTimeKind.Utc));
        entry["Version"] = sequence;
        return BsonSerializer.Deserialize<PropertyChangeEntry>(entry);
    }

    [Fact]
    public async Task RepeatedExtensionsUseTheFirstDateAndAcceptTheExactLimit()
    {
        DeadlineConfiguration().MaxPostponementDays = 28;
        _changes.Add(PropertyChangeEntry.Create("Deadline", BsonNull.Value, UnitTestsHelpers.AdminUser));
        Assert.Empty((await Extend("2000-01-15")).Errors);
        Assert.Empty((await Extend("2000-01-29")).Errors);
        Assert.Equal(PostponementError.MaximumExtensionExceeded, Assert.Single((await Extend("2000-01-30")).Errors));
        Assert.Equal(3, _changes.Count);
        var dto = await Factory().Create(_instance, _ct);
        Assert.Equal(new DateOnly(2000, 1, 29), dto.Steps.SelectMany(step => new[] { step }.Concat(step.Children ?? []))
            .Single(step => step.Id == "Start").Deadline!.MaxDate);
    }

    [Fact]
    public async Task AllDeadlinesUseEachStepLimitAndRejectBeforeAnyWrite()
    {
        DeadlineConfiguration().MaxPostponementDays = 28;
        DeadlineConfiguration("EndDate").MaxPostponementDays = 14;
        var result = await Postpone(Request() with
        {
            Changes =
            [
                new("Deadline", Original, new DateOnly(2000, 1, 16)),
                new("EndDate", Original.AddDays(10), new DateOnly(2000, 1, 26))
            ]
        });
        Assert.Equal(PostponementError.MaximumExtensionExceeded, Assert.Single(result.Errors));
        Assert.Empty(_changes);
        Assert.Equal(Original.UtcDateTime, _instance.Properties["Deadline"].ToUniversalTime());
        _mailServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ZeroDisablesPostponementWhileUnsetRemainsUnlimited()
    {
        DeadlineConfiguration().MaxPostponementDays = 0;
        Assert.Equal(PostponementError.MaximumExtensionExceeded, Assert.Single((await Extend("2000-01-02")).Errors));
        Assert.Empty((await Extend("2999-01-01", "EndDate")).Errors);
    }

    [Theory]
    [InlineData("2027-03-27T23:00:00Z", "2027-03-29")]
    [InlineData("2027-10-30T22:00:00Z", "2027-11-01")]
    public async Task MaximumCountsAmsterdamCalendarDays(string original, string maximum)
    {
        _instance.Properties["Deadline"] = new BsonDateTime(DateTimeOffset.Parse(original).UtcDateTime);
        DeadlineConfiguration().MaxPostponementDays = 1;
        Assert.Empty((await Extend(maximum)).Errors);
        Assert.Equal(PostponementError.MaximumExtensionExceeded,
            Assert.Single((await Extend(DateOnly.Parse(maximum).AddDays(1).ToString("yyyy-MM-dd"))).Errors));
    }
}