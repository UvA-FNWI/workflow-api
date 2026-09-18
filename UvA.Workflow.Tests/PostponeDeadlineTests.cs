using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Moq;
using UvA.Workflow.Api.Actions;
using UvA.Workflow.Api.Actions.Dtos;
using UvA.Workflow.Api.Deadlines;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Submissions;
using UvA.Workflow.Api.Submissions.Dtos;
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
    private readonly PostponeDeadlineController _controller;
    private readonly SubmissionsController _submissions;
    private readonly SubmissionDtoFactory _submissionDtoFactory;
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
            Pages = [new Page { Name = "Reason", Fields = [reason, explanation] }]
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
            _service, Factory(), _modelService, _instanceService);
        _submissionDtoFactory = new(new ArtifactTokenService(UnitTestsHelpers.TestS3Config), _modelService);
        _submissions = new(_userServiceMock.Object, _modelService, _rightsService,
            new SubmissionService(_modelService, _instanceService, _instanceJournalServiceMock.Object, _jobService,
                _effectService),
            _workflowInstanceService, _submissionDtoFactory, Factory(), _answerService, new DummyAnswerGenerator());
    }

    private WorkflowInstanceDtoFactory Factory(string[]? roles = null) =>
        StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(
            _modelService, _workflowInstanceRepoMock, roles ?? ["Coordinator"],
            new(new InstanceJournalEntry { PropertyChanges = _changes.ToArray() }, []));

    private PostponeDeadlinesRequest Request(string reason = "Research delay") =>
        new([new("Deadline", Original, new DateOnly(2999, 1, 1))], reason);

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
            Assert.Equal(_form.Title, action.Title);
            Assert.Equal(_form.Name, action.Form);
            Assert.Equal(FormLayout.Modal, action.FormLayout);
            var loaded = await _controller.GetForm(_instance.Id, _action.Name!, _ct);
            Assert.Equal(2,
                Assert.IsType<FormDto>(Assert.IsType<OkObjectResult>(loaded.Result).Value).Pages[0].Questions[0]
                    .Choices!.Length);
        }

        if (!allowed)
        {
            var loaded = await _controller.GetForm(_instance.Id, _action.Name!, _ct);
            Assert.Equal(403, Assert.IsType<ObjectResult>(loaded.Result).StatusCode);
        }

        var response = await _controller.Postpone(_instance.Id, _action.Name!, Request(), _ct);
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
        _form.Pages[0].Fields[0].Values![0].Condition = new() { Event = new() { Id = "AllowResearchReason" } };
        var loaded = await _controller.GetForm(_instance.Id, _action.Name!, _ct);
        var form = Assert.IsType<FormDto>(Assert.IsType<OkObjectResult>(loaded.Result).Value);
        Assert.Equal(2, form.Pages[0].Questions[0].Choices!.Length);
        Assert.Empty((await Postpone(Request("Research"))).Errors);
        Assert.Equal("Research", Assert.Single(_changes).Reason);
        Assert.False(_instance.Properties.ContainsKey("Reason"));
        Assert.False(_instance.Properties.ContainsKey("Explanation"));
    }

    [Theory]
    [InlineData(ActionType.Execute, 403)]
    [InlineData(ActionType.PostponeDeadlines, 400)]
    public async Task PostponementIsNotAvailableThroughTheActionsRoute(ActionType type, int status)
    {
        var controller = new ActionsController(_workflowInstanceRepoMock.Object, _userServiceMock.Object,
            _rightsService, _effectService, _jobService, Factory(), _instanceService);
        var response =
            await controller.ExecuteAction(new ExecuteActionInputDto(type, _instance.Id, _action.Name),
                _ct);
        Assert.Equal(status, Assert.IsType<ObjectResult>(response.Result).StatusCode);
        _mailServiceMock.VerifyNoOtherCalls();
        Assert.Empty(_changes);
    }

    [Fact]
    public async Task EndpointRejectsActionsWithoutAConfiguredForm()
    {
        _action.Form = null;
        var response = await _controller.Postpone(_instance.Id, _action.Name!, Request(), _ct);
        Assert.Equal(403, Assert.IsType<ObjectResult>(response.Result).StatusCode);
        Assert.Empty(_changes);
        _mailServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData("An unconfigured reason")]
    [InlineData("Research delay")]
    [InlineData("Other")]
    [InlineData("Overig\nWachten op onderzoeksgegevens")]
    public async Task SavesTheReasonStringAsProvided(string reason)
    {
        var response = await _controller.Postpone(_instance.Id, _action.Name!, Request(reason), _ct);
        Assert.IsType<OkObjectResult>(response.Result);
        Assert.Equal(reason, Assert.Single(_changes).Reason);
        Assert.False(_instance.Properties.ContainsKey("Reason"));
        Assert.False(_instance.Properties.ContainsKey("Explanation"));
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
    public async Task InvalidRequestReturnsErrorCodesThroughTheApi()
    {
        var response = await _controller.Postpone(_instance.Id, _action.Name!, Request() with { Changes = [] }, _ct);
        var result = Assert.IsType<UnprocessableEntityObjectResult>(response.Result);
        Assert.Equal(PostponementError.InvalidChanges, Assert.Single(Assert.IsType<PostponementError[]>(result.Value)));
        Assert.Empty(_changes);
        _mailServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NonexistentAmsterdamTimeDoesNotWriteOrSendMail()
    {
        var previous = DateTimeOffset.Parse("2027-01-01T02:30:00+01:00");
        _instance.Properties["Deadline"] = new BsonDateTime(previous.UtcDateTime);
        var result =
            await Postpone(Request() with { Changes = [new("Deadline", previous, new DateOnly(2027, 3, 28))] });
        Assert.Equal(PostponementError.InvalidChanges, Assert.Single(result.Errors));
        Assert.Equal(previous.UtcDateTime, _instance.Properties["Deadline"].ToUniversalTime());
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
    public async Task FailedMailCanBeRetriedWithoutChangingTheDatesAgain()
    {
        Job? savedJob = null;
        _jobRepositoryMock.Setup(repo => repo.Add(It.IsAny<Job>(), It.IsAny<CancellationToken>()))
            .Callback<Job, CancellationToken>((job, _) => savedJob = job).Returns(Task.CompletedTask);
        _userRepoMock.Setup(repo => repo.GetById(UnitTestsHelpers.AdminUser.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UnitTestsHelpers.AdminUser);
        _mailServiceMock.SetupSequence(service => service.Send(It.IsAny<MailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Mail unavailable"))
            .ReturnsAsync(new MailDispatchResult([], [], [], null));

        var result = await Postpone(Request());
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Effects!.Error);
        Assert.NotNull(savedJob);
        Assert.Equal(JobStatus.Failed, savedJob.Status);
        var postponedDate = _instance.Properties["Deadline"];

        // The jobs endpoint queues a fresh attempt with the same source and step identifiers.
        savedJob.Status = JobStatus.Pending;
        await _jobService.RunJob(savedJob, _ct);
        Assert.Equal(JobStatus.Completed, savedJob.Status);
        Assert.Equal(postponedDate, _instance.Properties["Deadline"]);
        Assert.Single(_changes);
        _mailServiceMock.Verify(service => service.Send(It.IsAny<MailMessage>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task CompletedDatesCanMoveWithoutLosingCompletionOrAmsterdamTime()
    {
        _instance.Events["Start"] = new() { Id = "Start", Date = new DateTime(2000, 1, 1) };
        var result = await Postpone(Request() with
        {
            Changes =
            [
                new("Deadline", Original, new DateOnly(2027, 7, 1)),
                new("EndDate", Original.AddDays(10), new DateOnly(2027, 7, 1))
            ]
        });
        Assert.Empty(result.Errors);
        Assert.Equal(DateTimeOffset.Parse("2027-07-01T12:00:00+02:00").UtcDateTime,
            _instance.Properties["Deadline"].ToUniversalTime());
        Assert.NotNull(_instance.Events["Start"].Date);
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
    public async Task FormReadResolvesIntroductionWithoutWritesOrSubmitRights()
    {
        _instance.Properties["Course"] = "course-id";
        _form.Pages[0].Introduction = new("Extend for {{ Course.Name }}.", "Uitstel voor {{ Course.Name }}.");
        _workflowInstanceRepoMock.Setup(repo => repo.GetAllById(It.Is<string[]>(ids => ids.Contains("course-id")),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Dictionary<string, BsonValue> { ["_id"] = "course-id", ["Name"] = "Research Methods" }]);
        var previous = _instance.Properties.ToBsonDocument();
        var response = await _controller.GetForm(_instance.Id, _action.Name!, _ct);
        var form = Assert.IsType<FormDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal("Extend for Research Methods.", form.Pages[0].Introduction!.En);
        Assert.Equal("Uitstel voor Research Methods.", form.Pages[0].Introduction!.Nl);
        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            _submissions.SubmitSubmission(_instance.Id, _form.Name, _ct));
        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            _submissions.GetSubmission(_instance.Id, _form.Name, version: 1, ct: _ct));
        var answers = new AnswersController(_answerService, _rightsService, _submissionDtoFactory,
            _instanceService, _modelService, _workflowInstanceRepoMock.Object, _workflowInstanceService);
        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            answers.SaveAnswer(_instance.Id, _form.Name, "Reason", new(null), _ct));
        Assert.Equal(previous, _instance.Properties.ToBsonDocument());
        Assert.Empty(_instance.Events);
        Assert.Empty(_changes);
        _mailServiceMock.VerifyNoOtherCalls();
        _jobRepositoryMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("unconfigured")]
    [InlineData("differentAction")]
    [InlineData("unnamed")]
    [InlineData("step")]
    [InlineData("condition")]
    [InlineData("execute")]
    public async Task FormReadRequiresAnAvailableMatchingGlobalAction(string scenario)
    {
        switch (scenario)
        {
            case "unconfigured": _action.Form = null; break;
            case "differentAction": _action.Name = "DifferentAction"; break;
            case "unnamed": _action.Name = null; break;
            case "step": _action.Steps = ["Start"]; break;
            case "condition": _action.Condition = new() { Event = new() { Id = "NotYetAvailable" } }; break;
            case "execute": _action.Type = RoleAction.Execute; break;
        }

        var response = await _controller.GetForm(_instance.Id, "GrantExtension", _ct);
        Assert.Equal(403, Assert.IsType<ObjectResult>(response.Result).StatusCode);
    }

    private PropertyDefinition DeadlineProperty(string name = "Deadline") =>
        _modelService.WorkflowDefinitions["Project"].Properties.Single(p => p.Name == name);

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
        var response = await _controller.Postpone(_instance.Id, _action.Name!, Request() with
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
            DeadlineProperty(), DeadlineConfiguration().MaxPostponementDays, Original.AddDays(10), journal));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("otherProperty")]
    [InlineData("emptyValues")]
    public void MaximumFallsBackToCurrentValueOnlyWithoutADatedLogForThatDeadline(string history)
    {
        DeadlineConfiguration().MaxPostponementDays = 14;
        InstanceJournalEntry? journal = history == "missing" ? null : new();
        if (history == "otherProperty") journal!.PropertyChanges = [LoggedDate("EndDate", Original, 1)];
        if (history == "emptyValues")
            journal!.PropertyChanges =
            [
                PropertyChangeEntry.Create("Deadline", null, UnitTestsHelpers.AdminUser),
                PropertyChangeEntry.Create("Deadline", BsonNull.Value, UnitTestsHelpers.AdminUser),
                PropertyChangeEntry.Create("Deadline", new BsonString(""), UnitTestsHelpers.AdminUser)
            ];
        Assert.Equal(new DateOnly(2000, 1, 22), PostponeDeadlineService.GetMaximumDate(
            DeadlineProperty(), DeadlineConfiguration().MaxPostponementDays, Original.AddDays(7), journal));
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