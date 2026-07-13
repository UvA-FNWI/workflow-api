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
    public async Task Create_UsesHistoricalViewRightsForVersionSubmissions()
    {
        var submittedAt = DateTime.UtcNow.AddMinutes(-10);
        var instance = CreateProjectInstance();
        var stepVersionService = CreateStepVersionService("Start", new StepVersion
        {
            VersionNumber = 1,
            EventIds = ["Start"],
            SubmittedAt = submittedAt
        });
        var factory = CreateFactory(stepVersionService.Object);

        MockCurrentUser("Coordinator");
        MockInstance(instance);
        MockEmptyRelatedInstanceLookups();
        MockNoPropertyJournal(instance);
        MockHistoricalEventLog(instance, EventLog(instance, "Start", submittedAt));

        var dto = await factory.Create(instance, CancellationToken.None);

        var startStep = FindStep(dto.Steps, "Start");
        var version = Assert.Single(startStep.Versions!);
        Assert.NotEmpty(version.Submissions);
        Assert.All(version.Submissions, submission => Assert.Equal("Start", submission.FormName));
        Assert.All(version.Submissions, submission => Assert.Equal(submittedAt, submission.DateSubmitted));
    }

    [Fact]
    public async Task Create_UsesMatchesFormForHistoricalVersionViewRights()
    {
        var submittedAt = DateTime.UtcNow.AddMinutes(-10);
        var instance = CreateProjectInstance();
        var stepVersionService = CreateStepVersionService("Start", new StepVersion
        {
            VersionNumber = 1,
            EventIds = ["Start"],
            SubmittedAt = submittedAt
        });
        var factory = CreateFactory(stepVersionService.Object);

        _modelParser.Roles.Add(new Role
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
        });
        MockCurrentUser("WildcardViewer");
        MockInstance(instance);
        MockEmptyRelatedInstanceLookups();
        MockNoPropertyJournal(instance);
        MockHistoricalEventLog(instance, EventLog(instance, "Start", submittedAt));

        var dto = await factory.Create(instance, CancellationToken.None);

        var startStep = FindStep(dto.Steps, "Start");
        var version = Assert.Single(startStep.Versions!);
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
            EventIds = ["ProposalApprovedSupervisor"],
            SubmittedAt = submittedAt
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
        var version = Assert.Single(supervisorStep.Versions!);
        var submission = Assert.Single(version.Submissions);
        var proposalSufficient = submission.Answers.Single(answer => answer.QuestionName == "ProposalSufficient");
        Assert.Equal("Pass", proposalSufficient.Value?.GetString());
    }

    private WorkflowInstanceDtoFactory CreateFactory(IStepVersionService stepVersionService)
    {
        var submissionDtoFactory =
            new SubmissionDtoFactory(new ArtifactTokenService(UnitTestsHelpers.TestS3Config), _modelService);

        return new WorkflowInstanceDtoFactory(
            _instanceService,
            _modelService,
            submissionDtoFactory,
            _workflowInstanceRepoMock.Object,
            _rightsService,
            stepVersionService,
            new StepHeaderStatusResolver(_modelService),
            _workflowInstanceService,
            NullLogger<WorkflowInstanceDtoFactory>.Instance);
    }

    private static Mock<IStepVersionService> CreateStepVersionService(
        string stepName,
        StepVersion version)
    {
        var stepVersionService = new Mock<IStepVersionService>();
        stepVersionService
            .Setup(s => s.GetStepVersions(
                It.IsAny<WorkflowInstance>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        stepVersionService
            .Setup(s => s.GetStepVersions(
                It.IsAny<WorkflowInstance>(),
                It.IsAny<string>(),
                It.IsAny<IEnumerable<InstanceEventLogEntry>>()))
            .Returns([]);
        stepVersionService
            .Setup(s => s.GetStepVersions(
                It.IsAny<WorkflowInstance>(),
                stepName,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([version]);
        stepVersionService
            .Setup(s => s.GetStepVersions(
                It.IsAny<WorkflowInstance>(),
                stepName,
                It.IsAny<IEnumerable<InstanceEventLogEntry>>()))
            .Returns([version]);
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