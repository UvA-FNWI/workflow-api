using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure.S3;
using UvA.Workflow.Journaling;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.Versioning;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;
using DomainAction = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.Tests;

public class WorkflowInstanceDtoFactoryVersionAccessTests : ControllerTestsBase
{
    [Fact]
    public async Task Create_LiveSubmissionIncludesGroupedAnswerHistory()
    {
        var submittedAt = DateTime.UtcNow.AddMinutes(-10);
        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Start")
            .WithProperties(("EC", _ => 12))
            .WithEvent("Start", submittedAt)
            .Build();
        var change = PropertyChangeEntry.Create("EC", 6, UnitTestsHelpers.AdminUser);
        var factory = CreateFactory(Mock.Of<IStepVersionService>());

        MockCurrentUser("Coordinator");
        MockInstance(instance);
        MockEmptyRelatedInstanceLookups();
        MockPropertyJournal(instance, (change, 1));
        MockHistoricalEventLog(instance, EventLog(instance, "Start", submittedAt));

        var dto = await factory.Create(instance, CancellationToken.None);

        var start = dto.Submissions.Single(submission => submission.FormName == "Start");
        var ec = start.Answers.Single(answer => answer.QuestionName == "EC");
        var group = Assert.Single(ec.Changes!);
        Assert.Equal(1, group.VersionNumber);
        Assert.Equal(12, group.Changes[0].Value?.GetInt32());
        Assert.Equal(6, group.Changes[1].Value?.GetInt32());
    }

    [Fact]
    public async Task Create_UndoingRejectionKeepsDraftEditsOutOfLiveAnswerCorrections()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var instance = CreateProjectInstance();
        instance.CreatedOn = t;
        instance.Properties["EC"] = 18;
        var rejection = EventLog(instance, "RejectSubject", t.AddMinutes(3));
        rejection.OperationId = "rejection";
        var logs = new[]
        {
            EventLog(instance, "Start", t.AddMinutes(1)),
            rejection,
            new InstanceEventLogEntry
            {
                WorkflowInstanceId = instance.Id, Timestamp = t.AddMinutes(5),
                Operation = EventLogOperation.Undo, OperationId = rejection.OperationId
            }
        };
        instance.Events = EventHistory.RebuildEvents(logs);
        var correction = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<PropertyChangeEntry>(new BsonDocument
        {
            ["Timestamp"] = t.AddMinutes(2), ["Path"] = "EC", ["OldValue"] = 6,
            ["ModifiedBy"] = "coordinator"
        });
        var draft = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<PropertyChangeEntry>(new BsonDocument
        {
            ["Timestamp"] = t.AddMinutes(4), ["Path"] = "EC", ["OldValue"] = 12,
            ["ModifiedBy"] = "coordinator"
        });
        MockCurrentUser("Coordinator");
        MockInstance(instance);
        MockEmptyRelatedInstanceLookups();
        MockPropertyJournal(instance, (correction, 1), (draft, 1));
        MockHistoricalEventLog(instance, logs);

        var dto = await CreateFactory(new StepVersionService(_modelService)).Create(instance, CancellationToken.None);

        var versions = FindStep(dto.Steps, "Subject").Versions!;
        Assert.Equal(1, versions.Current!.VersionNumber);
        Assert.Empty(versions.History);
        foreach (var submission in new[]
                 {
                     dto.Submissions.Single(s => s.FormName == "Start"),
                     versions.Current.Submissions.Single(s => s.FormName == "Start")
                 })
        {
            var answer = submission.Answers.Single(a => a.QuestionName == "EC");
            Assert.Equal(18, answer.Value?.GetInt32());
            var changes = Assert.Single(answer.Changes!).Changes;
            Assert.Equal([12, 6], changes.Select(change => change.Value!.Value.GetInt32()));
        }
    }

    [Fact]
    public async Task Create_UsesCurrentViewRightsForVersionSubmissions()
    {
        var submittedAt = DateTime.UtcNow.AddMinutes(-10);
        var instance = CreateProjectInstance();
        var stepVersionService = CreateStepVersionService("Start", new StepVersion
        {
            VersionNumber = 1,
            Submissions = [new("Start", submittedAt, "Start", "Start")],
            FrozenAt = submittedAt
        });
        var factory = CreateFactory(stepVersionService.Object);

        MockCurrentUser("Coordinator");
        MockInstance(instance);
        MockEmptyRelatedInstanceLookups();
        MockNoPropertyJournal(instance);
        MockHistoricalEventLog(instance, EventLog(instance, "Start", submittedAt));

        var dto = await factory.Create(instance, CancellationToken.None);

        var startStep = FindStep(dto.Steps, "Start");
        var version = Assert.Single(startStep.Versions!.History);
        Assert.NotEmpty(version.Submissions);
        Assert.All(version.Submissions, submission => Assert.Equal("Start", submission.FormName));
        Assert.All(version.Submissions, submission => Assert.Equal(submittedAt, submission.DateSubmitted));
        Assert.All(version.Submissions.SelectMany(submission => submission.Answers),
            answer => Assert.Null(answer.Changes));
    }

    [Fact]
    public async Task Create_UsesMatchesFormForHistoricalVersionViewRights()
    {
        var submittedAt = DateTime.UtcNow.AddMinutes(-10);
        var instance = CreateProjectInstance();
        var stepVersionService = CreateStepVersionService("Start", new StepVersion
        {
            VersionNumber = 1,
            Submissions = [new("Start", submittedAt, "Start", "Start")],
            FrozenAt = submittedAt
        });
        var factory = CreateFactory(stepVersionService.Object);

        var wildcardViewer = new Role
        {
            Name = "WildcardViewer",
            Actions =
            [
                new DomainAction
                {
                    Type = RoleAction.View,
                    Form = DomainAction.All
                }
            ]
        };
        _modelService.WorkflowDefinitions["Project"].Roles.Add(wildcardViewer);

        MockCurrentUser("WildcardViewer");
        MockInstance(instance);
        MockEmptyRelatedInstanceLookups();
        MockNoPropertyJournal(instance);
        MockHistoricalEventLog(instance, EventLog(instance, "Start", submittedAt));

        var dto = await factory.Create(instance, CancellationToken.None);

        var startStep = FindStep(dto.Steps, "Start");
        var version = Assert.Single(startStep.Versions!.History);
        Assert.NotEmpty(version.Submissions);
        Assert.All(version.Submissions, submission => Assert.Equal("Start", submission.FormName));
        Assert.All(version.Submissions, submission => Assert.Equal(submittedAt, submission.DateSubmitted));
    }

    [Fact]
    public async Task Create_UsesHistoricalNestedAnswerValuesForVersionSubmissions()
    {
        var submittedAt = DateTime.UtcNow.AddMinutes(-10);
        var instance = CreateRmssInstanceWithSupervisorAssessment("Fail");
        var stepVersionService = CreateStepVersionService("ProposalAssessmentSupervisor", new StepVersion
        {
            VersionNumber = 1,
            Submissions =
            [
                new("ProposalAndEthicsReviewSupervisor", submittedAt, "ProposalApprovedSupervisor",
                    "ProposalAssessmentSupervisor")
            ],
            FrozenAt = submittedAt
        });
        var factory = CreateFactory(stepVersionService.Object);

        MockCurrentUser("Supervisor");
        MockInstance(instance);
        MockEmptyRelatedInstanceLookups();
        MockPropertyJournal(
            instance,
            (PropertyChangeEntry.Create(
                    "SupervisorProposalAndEthicsReview.ProposalSufficient",
                    BsonValue.Create("Pass"),
                    UnitTestsHelpers.AdminUser),
                1));
        MockHistoricalEventLog(instance, EventLog(instance, "ProposalApprovedSupervisor", submittedAt));

        var dto = await factory.Create(instance, CancellationToken.None);

        var supervisorStep = FindStep(dto.Steps, "ProposalAssessmentSupervisor");
        var version = Assert.Single(supervisorStep.Versions!.History);
        var submission = Assert.Single(version.Submissions);
        var proposalSufficient = submission.Answers.Single(answer => answer.QuestionName == "ProposalSufficient");
        Assert.Equal("Pass", proposalSufficient.Value?.GetString());
    }

    [Fact]
    public async Task Create_MapsCurrentSeparatelyFromHistory()
    {
        var historicalSubmittedAt = DateTime.UtcNow.AddMinutes(-20);
        var currentSubmittedAt = DateTime.UtcNow.AddMinutes(-10);
        var instance = CreateProjectInstance();
        instance.Events["Start"] = new InstanceEvent
        {
            Id = "Start",
            Date = currentSubmittedAt
        };
        var stepVersionService = CreateStepVersionService(
            "Start",
            new StepVersion
            {
                VersionNumber = 1,
                Submissions = [new("Start", historicalSubmittedAt, "Start", "Start")],
                FrozenAt = historicalSubmittedAt
            },
            new StepVersion
            {
                VersionNumber = 2,
                Submissions = [new("Start", currentSubmittedAt, "Start", "Start")]
            });
        var factory = CreateFactory(stepVersionService.Object);

        MockCurrentUser("Coordinator");
        MockInstance(instance);
        MockEmptyRelatedInstanceLookups();
        MockNoPropertyJournal(instance);
        MockHistoricalEventLog(
            instance,
            EventLog(instance, "Start", historicalSubmittedAt),
            EventLog(instance, "Start", currentSubmittedAt));

        var dto = await factory.Create(instance, CancellationToken.None);

        var startStep = FindStep(dto.Steps, "Start");
        var historicalVersion = Assert.Single(startStep.Versions!.History);
        Assert.Equal(1, historicalVersion.VersionNumber);
    }

    [Fact]
    public async Task Create_RealReconstructionFreezesCorrectionsAtRestartAndUsesCurrentViewPermission()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var instance = CreateProjectInstance();
        instance.CreatedOn = t;
        instance.Properties["Subject"] = "Corrected";
        instance.Events["Start"] = new InstanceEvent { Id = "Start", Date = t.AddMinutes(1) };
        instance.Events["RejectSubject"] = new InstanceEvent { Id = "RejectSubject", Date = t.AddMinutes(3) };
        var definition = _modelService.WorkflowDefinitions["Project"];
        var view = new DomainAction
        {
            Type = RoleAction.View, Form = "Start",
            Condition = new() { Event = new() { Id = "RejectSubject" } }
        };
        definition.Roles.Add(new Role { Name = "PresentViewer", Actions = [view] });
        MockCurrentUser("PresentViewer");
        MockInstance(instance);
        MockEmptyRelatedInstanceLookups();
        var correction = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<PropertyChangeEntry>(new BsonDocument
        {
            ["Timestamp"] = t.AddMinutes(2), ["Path"] = "Subject", ["OldValue"] = "Original",
            ["ModifiedBy"] = "coordinator", ["Version"] = 1
        });
        MockPropertyJournal(instance, (correction, 1));
        MockHistoricalEventLog(instance, EventLog(instance, "Start", t.AddMinutes(1)),
            EventLog(instance, "RejectSubject", t.AddMinutes(3)));
        var factory = CreateFactory(new StepVersionService(_modelService));

        var dto = await factory.Create(instance, CancellationToken.None);
        var subject = FindStep(dto.Steps, "Subject");
        Assert.Equal(2, subject.Versions!.Current!.VersionNumber);
        Assert.Empty(subject.Versions.Current.Submissions);
        Assert.Null(FindStep(dto.Steps, "Start").Versions);
        var history = Assert.Single(subject.Versions.History);
        var submission = Assert.Single(history.Submissions);
        Assert.Equal("Corrected", submission.Answers.Single(a => a.QuestionName == "Subject").Value?.GetString());
        Assert.Empty(submission.Permissions);

        view.Condition!.Not = true;
        dto = await factory.Create(instance, CancellationToken.None);
        subject = FindStep(dto.Steps, "Subject");
        Assert.Equal(2, subject.Versions!.Current!.VersionNumber);
        Assert.Empty(Assert.Single(subject.Versions.History).Submissions);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    public async Task Create_SavingANewDraftAfterRestartPreservesTheCorrectedPreviousAttempt(int lastDraftEditMinute)
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var instance = CreateProjectInstance();
        instance.CreatedOn = t;
        instance.Properties["Subject"] = "test2";
        var logs = new[]
        {
            EventLog(instance, "Start", t.AddMinutes(1)),
            EventLog(instance, "ApproveSubject", t.AddMinutes(1.5)),
            EventLog(instance, "RejectSubject", t.AddMinutes(3))
        };
        instance.Events = EventHistory.RebuildEvents(logs);
        MockCurrentUser("Coordinator");
        MockInstance(instance);
        MockEmptyRelatedInstanceLookups();
        var correction = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<PropertyChangeEntry>(new BsonDocument
        {
            ["Timestamp"] = t.AddMinutes(2), ["Path"] = "Subject", ["OldValue"] = "test",
            ["ModifiedBy"] = "coordinator", ["Version"] = 1
        });
        var draft = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<PropertyChangeEntry>(new BsonDocument
        {
            ["Timestamp"] = t.AddMinutes(lastDraftEditMinute),
            ["Path"] = "Subject", ["OldValue"] = "test2",
            ["ModifiedBy"] = "coordinator", ["Version"] = 1
        });
        MockPropertyJournal(instance, (correction, 1));
        MockHistoricalEventLog(instance, logs);
        var factory = CreateFactory(new StepVersionService(_modelService));

        var beforeEditing = await factory.Create(instance, CancellationToken.None);
        Assert.Equal(2, FindStep(beforeEditing.Steps, "Subject").Versions!.Current!.VersionNumber);

        instance.Properties["Subject"] = "New draft";
        MockPropertyJournal(instance, (correction, 1), (draft, 1));
        var dto = await factory.Create(instance, CancellationToken.None);

        var subject = FindStep(dto.Steps, "Subject");
        Assert.NotNull(subject.Versions);
        Assert.Equal(2, subject.Versions.Current!.VersionNumber);
        Assert.Empty(subject.Versions.Current.Submissions);
        var previous = Assert.Single(subject.Versions.History);
        Assert.Equal(1, previous.VersionNumber);
        Assert.Equal(t.AddMinutes(1.5), previous.CompletionTimestamp);
        var submission = Assert.Single(previous.Submissions, submission => submission.FormName == "Start");
        Assert.Equal("test2",
            submission.Answers.Single(answer => answer.QuestionName == "Subject").Value?.GetString());
    }

    [Fact]
    public async Task Create_UnreconstructableProgressionKeepsNormalSubmissionDisplayWithoutInventingAttempts()
    {
        var submittedAt = DateTime.UtcNow.AddMinutes(-10);
        var instance = CreateProjectInstance();
        instance.Properties["Title"] = "Visible";
        instance.Events["Start"] = new InstanceEvent { Id = "Start", Date = submittedAt };
        _modelService.WorkflowDefinitions["Project"].AllSteps.Single(step => step.Name == "Subject").Condition =
            new() { Value = new() { Property = "Title", Equal = "=Visible" } };
        MockCurrentUser("Coordinator");
        MockInstance(instance);
        MockEmptyRelatedInstanceLookups();
        MockPropertyJournal(instance, (PropertyChangeEntry.Create("Title", "Hidden", UnitTestsHelpers.AdminUser), 1));
        MockHistoricalEventLog(instance, EventLog(instance, "Start", submittedAt));

        var dto = await CreateFactory(new StepVersionService(_modelService)).Create(instance, CancellationToken.None);

        var subject = FindStep(dto.Steps, "Subject");
        Assert.Null(subject.Versions);
        Assert.Contains(subject.Submissions!, submission => submission.FormName == "Start");
        Assert.Null(FindStep(dto.Steps, "Start").Versions);
    }

    [Fact]
    public async Task Create_ResolvesActionAttachedFormsForNonOwningChildRows()
    {
        var submittedAt = DateTime.UtcNow.AddMinutes(-10);
        var instance = CreateProjectInstance();
        instance.Events["Start"] = new InstanceEvent { Id = "Start", Date = submittedAt };
        _modelService.GetForm(instance, "Start").Step = null;
        MockCurrentUser("Coordinator");
        MockInstance(instance);
        MockEmptyRelatedInstanceLookups();
        MockNoPropertyJournal(instance);
        MockHistoricalEventLog(instance, EventLog(instance, "Start", submittedAt));

        var dto = await CreateFactory(new StepVersionService(_modelService)).Create(instance, CancellationToken.None);

        var start = FindStep(dto.Steps, "Start");
        Assert.Null(start.Versions);
        var submission = Assert.Single(start.Submissions!);
        Assert.Equal("Start", submission.Form.Step);
        Assert.Equal("Start", submission.FormName);
    }

    [Fact]
    public async Task Create_FiltersInfoCardsBySourcesAndExcludedSources()
    {
        var instance = CreateProjectInstance();
        var definition = _modelService.WorkflowDefinitions["Project"];
        definition.InfoCards = definition.InfoCards.Concat(new[]
        {
            new InfoCard
            {
                Name = "ExaminerOnly",
                Type = InfoCardType.Text,
                Title = "Examiner only",
                Sources = ["Examiner"],
                Content = "Visible only to examiners"
            },
            new InfoCard
            {
                Name = "AllExceptStudent",
                Type = InfoCardType.Text,
                Title = "All except student",
                ExcludedSources = ["Student"],
                Content = "Visible to everyone except students"
            },
            new InfoCard
            {
                Name = "StudentOnly",
                Type = InfoCardType.Text,
                Title = "Student only",
                Sources = ["Student"],
                Content = "Visible only to students"
            }
        }).ToArray();

        var factory = CreateFactory(Mock.Of<IStepVersionService>());

        MockCurrentUser("Student");
        MockInstance(instance);
        MockEmptyRelatedInstanceLookups();
        MockNoPropertyJournal(instance);
        MockHistoricalEventLog(instance);

        var studentDto = await factory.Create(instance, CancellationToken.None);

        var studentCardNames = studentDto.InfoCards.Select(card => card.Name).ToArray();
        Assert.Contains("StudentOnly", studentCardNames);
        Assert.DoesNotContain("ExaminerOnly", studentCardNames);
        Assert.DoesNotContain("AllExceptStudent", studentCardNames);

        MockCurrentUser("Examiner");

        var examinerDto = await factory.Create(instance, CancellationToken.None);
        var examinerCardNames = examinerDto.InfoCards.Select(card => card.Name).ToArray();
        Assert.Contains("ExaminerOnly", examinerCardNames);
        Assert.Contains("AllExceptStudent", examinerCardNames);
        Assert.DoesNotContain("StudentOnly", examinerCardNames);
    }

    [Fact]
    public async Task Create_KeepsEditableEmptyRequiredStaff()
    {
        var instance = CreateProjectInstance();
        var factory = CreateFactory(Mock.Of<IStepVersionService>());

        MockCurrentUser("SystemAdmin");
        MockInstance(instance);
        MockEmptyRelatedInstanceLookups();
        MockNoPropertyJournal(instance);
        MockHistoricalEventLog(instance);

        var dto = await factory.Create(instance, CancellationToken.None);

        var card = Assert.Single(dto.InfoCards, card => card.Name == "Staff");
        var supervisor = Assert.Single(card.Groups!.SelectMany(group => group.UserRoles),
            role => role.Role == "Supervisor");
        Assert.Empty(supervisor.Users);
        Assert.True(supervisor.CanEdit);
        Assert.False(supervisor.AllowsAssignment);
    }

    private WorkflowInstanceDtoFactory CreateFactory(IStepVersionService stepVersionService)
    {
        var submissionDtoFactory =
            new SubmissionDtoFactory(new ArtifactTokenService(UnitTestsHelpers.TestS3Config), _modelService,
                _instanceService);

        return new WorkflowInstanceDtoFactory(
            _instanceService,
            _modelService,
            submissionDtoFactory,
            _rightsService,
            stepVersionService,
            new StepProgressResolver(_modelService),
            _workflowInstanceService,
            NullLogger<WorkflowInstanceDtoFactory>.Instance,
            _undoService);
    }

    private static Mock<IStepVersionService> CreateStepVersionService(
        string stepName,
        params StepVersion[] versions)
    {
        var stepVersionService = new Mock<IStepVersionService>();
        stepVersionService.Setup(s => s.GetStepVersions(It.IsAny<WorkflowInstance>(),
                It.IsAny<WorkflowInstanceHistory>()))
            .Returns(new Dictionary<string, StepVersions>
            {
                [stepName] = new(versions.LastOrDefault(v => v.FrozenAt == null),
                    versions.Where(v => v.FrozenAt != null).Reverse().ToList())
            });
        return stepVersionService;
    }

    private void MockNoPropertyJournal(WorkflowInstance instance)
    {
        _instanceJournalServiceMock
            .Setup(s => s.GetInstanceJournal(instance.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((InstanceJournalEntry?)null);
    }

    private void MockPropertyJournal(
        WorkflowInstance instance,
        params (PropertyChangeEntry Change, int Version)[] changes)
    {
        foreach (var (change, version) in changes)
            change.Version = version;

        _instanceJournalServiceMock
            .Setup(s => s.GetInstanceJournal(instance.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InstanceJournalEntry
            {
                InstanceId = instance.Id,
                CurrentVersion = changes.Select(change => change.Version).DefaultIfEmpty(0).Max(),
                PropertyChanges = changes.Select(change => change.Change).ToArray()
            });
    }

    private void MockHistoricalEventLog(
        WorkflowInstance instance,
        params InstanceEventLogEntry[] eventLogs)
    {
        _eventRepoMock
            .Setup(r => r.GetEventLogEntriesForInstance(
                instance.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(eventLogs.OrderBy(log => log.Timestamp).ToList());
    }

    private static WorkflowInstance CreateProjectInstance()
    {
        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Start")
            .Build();
        instance.CreatedOn = DateTime.UtcNow;
        return instance;
    }

    private static WorkflowInstance CreateRmssInstanceWithSupervisorAssessment(string proposalSufficient)
    {
        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project-RMSS")
            .WithCurrentStep("ProposalAssessmentSupervisor")
            .WithProperties(
                ("SupervisorProposalAndEthicsReview", _ => new BsonDocument
                {
                    ["ProposalSufficient"] = proposalSufficient,
                    ["EthicsSufficient"] = "Pass"
                }))
            .Build();
        instance.CreatedOn = DateTime.UtcNow;
        return instance;
    }

    private static InstanceEventLogEntry EventLog(
        WorkflowInstance instance,
        string eventId,
        DateTime timestamp)
        => new()
        {
            WorkflowInstanceId = instance.Id,
            EventId = eventId,
            EventDate = timestamp,
            Operation = EventLogOperation.Create,
            Timestamp = timestamp
        };

    private static StepDto FindStep(IEnumerable<StepDto> steps, string id)
    {
        foreach (var step in steps)
        {
            if (step.Id == id)
                return step;

            if (step.Children == null)
                continue;

            var child = FindStepOrDefault(step.Children, id);
            if (child != null)
                return child;
        }

        throw new InvalidOperationException($"Step {id} not found");
    }

    private static StepDto? FindStepOrDefault(IEnumerable<StepDto> steps, string id)
    {
        foreach (var step in steps)
        {
            if (step.Id == id)
                return step;

            if (step.Children == null)
                continue;

            var child = FindStepOrDefault(step.Children, id);
            if (child != null)
                return child;
        }

        return null;
    }
}