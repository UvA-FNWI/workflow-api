using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Journaling;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;

namespace UvA.Workflow.Tests.Submissions;

public class SubmissionDtoFactoryChangeTests
{
    [Fact]
    public void Create_AttachesPostSubmitJournalChangesToAnswer()
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

        var dto = factory.Create(instance, form, state, modelService.GetQuestionStatus(instance, form, true),
            journal: journal);

        var ec = dto.Answers.Single(a => a.QuestionName == "EC");
        Assert.NotNull(ec.Changes);
        Assert.Equal(2, ec.Changes.Length);
        Assert.Equal(12, ec.Changes[0].Value?.GetInt32());
        Assert.Equal(change.Timestamp, ec.Changes[0].ChangedAt);
        Assert.Equal("admin", ec.Changes[0].ChangedBy);
        Assert.Equal(6, ec.Changes[1].Value?.GetInt32());
        Assert.Equal(1, ec.Changes[1].Version);
    }

    [Fact]
    public async Task CreateAsync_ResolvesChangedByDisplayName()
    {
        var modelService = new ModelService(UnitTestsHelpers.CreateModelParser());
        var users = new Mock<IUserService>();
        users.Setup(s => s.GetUser("admin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { UserName = "admin", DisplayName = "Ada Admin" });
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

        var dto = await factory.CreateAsync(instance, form, state,
            modelService.GetQuestionStatus(instance, form, true), journal: journal);

        var ec = dto.Answers.Single(a => a.QuestionName == "EC");
        Assert.Equal("Ada Admin", ec.Changes![0].ChangedBy);
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
}