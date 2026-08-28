using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Events;
using UvA.Workflow.Journaling;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests.Submissions;

public class SubmissionDtoFactoryChangeTests
{
    [Fact]
    public void Create_AttachesJournalChangesGroupedByFormSubmit()
    {
        var modelService = new ModelService(UnitTestsHelpers.CreateModelParser());
        var factory = new SubmissionDtoFactory(new ArtifactTokenService(UnitTestsHelpers.TestS3Config), modelService);
        var submitted = new DateTime(2026, 3, 1, 10, 0, 0);
        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Start")
            .WithProperties(("EC", _ => 12))
            .WithEvent("Start", submitted)
            .Build();
        var form = modelService.GetForm(instance, "Start");
        var state = FormSubmissionState.Resolve(instance, form, modelService.WorkflowDefinitions["Project"]);
        var change = PropertyChangeEntry.Create("EC", 6, UnitTestsHelpers.AdminUser);
        var journal = new InstanceJournalEntry
        {
            PropertyChanges = [change]
        };
        var history = new WorkflowInstanceHistory(journal,
        [
            new InstanceEventLogEntry
            {
                EventId = "Start",
                Timestamp = submitted,
                EventDate = submitted,
                Operation = EventLogOperation.Create
            }
        ]);

        var dto = factory.Create(instance, form, state, modelService.GetQuestionStatus(instance, form, true),
            history: history);

        var ec = dto.Answers.Single(a => a.QuestionName == "EC");
        Assert.NotNull(ec.Changes);
        var group = Assert.Single(ec.Changes);
        Assert.Equal(1, group.VersionNumber);
        Assert.Equal(2, group.Changes.Length);
        Assert.Equal(12, group.Changes[0].Value?.GetInt32());
        Assert.Equal(change.Timestamp, group.Changes[0].ChangedAt);
        Assert.Equal("admin", group.Changes[0].ChangedBy);
        Assert.Equal(6, group.Changes[1].Value?.GetInt32());
        Assert.Equal(submitted, group.Changes[1].ChangedAt);
    }

    [Fact]
    public async Task CreateAsync_ResolvesChangedByDisplayName()
    {
        var modelService = new ModelService(UnitTestsHelpers.CreateModelParser());
        var users = new Mock<IUserService>();
        users.Setup(s => s.GetUsers(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, User>
            {
                ["admin"] = new() { UserName = "admin", DisplayName = "Ada Admin" }
            });
        var factory = new SubmissionDtoFactory(new ArtifactTokenService(UnitTestsHelpers.TestS3Config), modelService,
            users.Object);
        var submitted = new DateTime(2026, 3, 1, 10, 0, 0);
        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Start")
            .WithProperties(("EC", _ => 12))
            .WithEvent("Start", submitted)
            .Build();
        var form = modelService.GetForm(instance, "Start");
        var state = FormSubmissionState.Resolve(instance, form, modelService.WorkflowDefinitions["Project"]);
        var journal = new InstanceJournalEntry
        {
            PropertyChanges = [PropertyChangeEntry.Create("EC", 6, UnitTestsHelpers.AdminUser)]
        };

        var history = new WorkflowInstanceHistory(journal,
        [
            new InstanceEventLogEntry
            {
                EventId = "Start",
                Timestamp = submitted,
                EventDate = submitted,
                Operation = EventLogOperation.Create
            }
        ]);
        var dto = await factory.CreateAsync(instance, form, state,
            modelService.GetQuestionStatus(instance, form, true), history: history);

        var ec = dto.Answers.Single(a => a.QuestionName == "EC");
        Assert.Equal("Ada Admin", ec.Changes![0].Changes[0].ChangedBy);
    }

    [Fact]
    public void Create_OmitsChangesWhenJournalNotProvided()
    {
        var modelService = new ModelService(UnitTestsHelpers.CreateModelParser());
        var factory = new SubmissionDtoFactory(new ArtifactTokenService(UnitTestsHelpers.TestS3Config), modelService);
        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Start")
            .WithProperties(("EC", _ => 12))
            .WithEvent("Start", new DateTime(2026, 3, 1, 10, 0, 0))
            .Build();
        var form = modelService.GetForm(instance, "Start");
        var state = FormSubmissionState.Resolve(instance, form, modelService.WorkflowDefinitions["Project"]);

        var dto = factory.Create(instance, form, state, modelService.GetQuestionStatus(instance, form, true));

        var ec = dto.Answers.Single(a => a.QuestionName == "EC");
        Assert.Null(ec.Changes);
    }

    [Fact]
    public void Create_KeepsPostSubmitEditAfterRejectAndResubmit()
    {
        var modelService = new ModelService(UnitTestsHelpers.CreateModelParser());
        var factory = new SubmissionDtoFactory(new ArtifactTokenService(UnitTestsHelpers.TestS3Config), modelService);
        var submittedV1 = new DateTime(2026, 8, 28, 11, 8, 38, DateTimeKind.Utc);
        var coordinatorEdit = submittedV1.AddSeconds(14);
        var rejected = submittedV1.AddSeconds(21);
        var studentEdit = submittedV1.AddSeconds(32);
        var submittedV2 = submittedV1.AddSeconds(33);
        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Start")
            .WithProperties(("Title", _ => "Second title"))
            .WithEvent("Start", submittedV2)
            .WithEvent("RejectSubject", rejected)
            .Build();
        var form = modelService.GetForm(instance, "Start");
        var state = FormSubmissionState.Resolve(instance, form, modelService.WorkflowDefinitions["Project"]);
        var history = new WorkflowInstanceHistory(
            new InstanceJournalEntry
            {
                PropertyChanges =
                [
                    Change("Title", "First title", coordinatorEdit),
                    Change("Title", "First title EDITED COORDINATOR", studentEdit)
                ]
            },
            [
                EventLog("Start", submittedV1, EventLogOperation.Create),
                EventLog("RejectSubject", rejected, EventLogOperation.Create),
                EventLog("Start", submittedV2, EventLogOperation.Update)
            ]);

        var dto = factory.Create(instance, form, state, modelService.GetQuestionStatus(instance, form, true),
            history: history);

        var title = dto.Answers.Single(answer => answer.QuestionName == "Title");
        Assert.NotNull(title.Changes);
        var version1 = Assert.Single(title.Changes, group => group.VersionNumber == 1);
        Assert.Collection(version1.Changes,
            edit =>
            {
                Assert.Equal("First title EDITED COORDINATOR", edit.Value?.GetString());
                Assert.Equal(coordinatorEdit, edit.ChangedAt);
            },
            submitted =>
            {
                Assert.Equal("First title", submitted.Value?.GetString());
                Assert.Equal(submittedV1, submitted.ChangedAt);
            });
        var version2 = Assert.Single(title.Changes, group => group.VersionNumber == 2);
        Assert.Equal("Second title", Assert.Single(version2.Changes).Value?.GetString());
    }

    [Fact]
    public void Create_OmitsEditsMadeAfterRejectionBeforeResubmission()
    {
        var modelService = new ModelService(UnitTestsHelpers.CreateModelParser());
        var workflowDefinition = modelService.WorkflowDefinitions["Project"];
        workflowDefinition.Events.Add(new EventDefinition { Name = "RejectStart", Suppresses = ["Start"] });
        var factory = new SubmissionDtoFactory(new ArtifactTokenService(UnitTestsHelpers.TestS3Config), modelService);
        var submittedV1 = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var rejected = submittedV1.AddMinutes(1);
        var edited = submittedV1.AddMinutes(2);
        var submittedV2 = submittedV1.AddMinutes(3);
        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Start")
            .WithProperties(("EC", _ => 12))
            .WithEvent("Start", submittedV2)
            .WithEvent("RejectStart", rejected)
            .Build();
        var form = modelService.GetForm(instance, "Start");
        var state = FormSubmissionState.Resolve(instance, form, workflowDefinition);
        var history = new WorkflowInstanceHistory(
            new InstanceJournalEntry { PropertyChanges = [Change("EC", 6, edited)] },
            [
                EventLog("Start", submittedV1, EventLogOperation.Create),
                EventLog("RejectStart", rejected, EventLogOperation.Create),
                EventLog("Start", submittedV2, EventLogOperation.Update)
            ]);

        var dto = factory.Create(instance, form, state, modelService.GetQuestionStatus(instance, form, true),
            history: history);

        Assert.Null(dto.Answers.Single(answer => answer.QuestionName == "EC").Changes);
    }

    private static PropertyChangeEntry Change(string path, BsonValue oldValue, DateTime timestamp)
        => BsonSerializer.Deserialize<PropertyChangeEntry>(new BsonDocument
        {
            [nameof(PropertyChangeEntry.Timestamp)] = timestamp,
            [nameof(PropertyChangeEntry.Path)] = path,
            [nameof(PropertyChangeEntry.OldValue)] = oldValue,
            [nameof(PropertyChangeEntry.ModifiedBy)] = UnitTestsHelpers.AdminUser.UserName
        });

    private static InstanceEventLogEntry EventLog(string eventId, DateTime timestamp,
        EventLogOperation operation)
        => new()
        {
            EventId = eventId,
            Timestamp = timestamp,
            EventDate = timestamp,
            Operation = operation
        };
}