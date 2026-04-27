using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Journaling;
using UvA.Workflow.Notifications;
using UvA.Workflow.Persistence;
using UvA.Workflow.Users;
using UvA.Workflow.Versioning;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests;

public class StepHeaderStatusTests
{
    [Fact]
    public void Resolver_ReturnsPendingStatusWhenPendingEventIsActive()
    {
        var modelService = CreateExampleModelService();
        var resolver = new StepHeaderStatusResolver(modelService);
        var instance = CreateProjectInstance(("Start", new DateTime(2026, 04, 14, 9, 0, 0, DateTimeKind.Utc)));

        var status = resolver.Resolve(GetStep(modelService, "Subject"), instance);

        Assert.NotNull(status);
        Assert.Equal(StepHeaderPillType.Info, status!.Type);
        Assert.Equal("Wait for approval", status.Label.En);
        Assert.Equal("Wacht op goedkeuring", status.Label.Nl);
    }

    [Fact]
    public void Resolver_ReturnsRejectedStatusWhenRejectedEventIsActive()
    {
        var modelService = CreateExampleModelService();
        var resolver = new StepHeaderStatusResolver(modelService);
        var instance = CreateProjectInstance(
            ("Start", new DateTime(2026, 04, 10, 9, 0, 0, DateTimeKind.Utc)),
            ("RejectSubject", new DateTime(2026, 04, 11, 9, 0, 0, DateTimeKind.Utc))
        );

        var status = resolver.Resolve(GetStep(modelService, "Subject"), instance);

        Assert.NotNull(status);
        Assert.Equal(StepHeaderPillType.Attention, status!.Type);
        Assert.Equal("Changes needed", status.Label.En);
        Assert.Equal("Aanpassingen nodig", status.Label.Nl);
    }

    [Fact]
    public void Resolver_LetsTemplateControlApprovedDateFormatting()
    {
        var modelService = CreateExampleModelService();
        var step = GetStep(modelService, "Subject");
        GetHeaderStatusConfiguration(step, "ApproveSubject").Label = new BilingualString(
            "Approved and assigned on {{ formatDate(ApproveSubjectEvent, =dd-MM-yyyy) }}",
            "Goedgekeurd en toegewezen op {{ formatDate(ApproveSubjectEvent, =dd-MM-yyyy) }}"
        );
        var resolver = new StepHeaderStatusResolver(modelService);
        var instance = CreateProjectInstance(
            ("Start", new DateTime(2026, 04, 10, 9, 0, 0, DateTimeKind.Utc)),
            ("RejectSubject", new DateTime(2026, 04, 11, 9, 0, 0, DateTimeKind.Utc)),
            ("ApproveSubject", new DateTime(2026, 04, 14, 9, 0, 0, DateTimeKind.Utc))
        );

        var status = resolver.Resolve(step, instance);

        Assert.NotNull(status);
        Assert.Equal(StepHeaderPillType.Success, status!.Type);
        Assert.Equal("Approved and assigned on 14-04-2026", status.Label.En);
        Assert.Equal("Goedgekeurd en toegewezen op 14-04-2026", status.Label.Nl);
    }

    [Fact]
    public void Resolver_PrefersPendingWhenRejectedEventIsSuppressedByResubmission()
    {
        var modelService = CreateExampleModelService();
        var resolver = new StepHeaderStatusResolver(modelService);
        var instance = CreateProjectInstance(
            ("Start", new DateTime(2026, 04, 14, 9, 0, 0, DateTimeKind.Utc)),
            ("RejectSubject", new DateTime(2026, 04, 13, 9, 0, 0, DateTimeKind.Utc))
        );

        var status = resolver.Resolve(GetStep(modelService, "Subject"), instance);

        Assert.NotNull(status);
        Assert.Equal(StepHeaderPillType.Info, status!.Type);
        Assert.Equal("Wait for approval", status.Label.En);
        Assert.Equal("Wacht op goedkeuring", status.Label.Nl);
    }

    [Fact]
    public void Resolver_ReturnsNullWhenStepHasNoHeaderStatusConfiguration()
    {
        var modelService = CreateExampleModelService();
        var resolver = new StepHeaderStatusResolver(modelService);
        var instance = CreateProjectInstance(("Start", new DateTime(2026, 04, 14, 9, 0, 0, DateTimeKind.Utc)));

        var status = resolver.Resolve(GetStep(modelService, "Upload"), instance);

        Assert.Null(status);
    }

    [Fact]
    public void Resolver_UsesConfiguredTypeWithoutChangingLabelResolution()
    {
        var modelService = CreateExampleModelService();
        var step = GetStep(modelService, "Subject");
        GetHeaderStatusConfiguration(step, "Start").Type = StepHeaderPillType.Attention;
        var resolver = new StepHeaderStatusResolver(modelService);
        var instance = CreateProjectInstance(("Start", new DateTime(2026, 04, 14, 9, 0, 0, DateTimeKind.Utc)));

        var status = resolver.Resolve(step, instance);

        Assert.NotNull(status);
        Assert.Equal(StepHeaderPillType.Attention, status!.Type);
        Assert.Equal("Wait for approval", status.Label.En);
        Assert.Equal("Wacht op goedkeuring", status.Label.Nl);
    }

    [Fact]
    public void Resolver_UsesConfiguredLabelTemplateWithoutChangingType()
    {
        var modelService = CreateExampleModelService();
        var step = GetStep(modelService, "Subject");
        GetHeaderStatusConfiguration(step, "Start").Label = new BilingualString(
            "Submitted on {{ formatDate(StartEvent, =dd-MM-yyyy) }}",
            "Ingediend op {{ formatDate(StartEvent, =dd-MM-yyyy) }}"
        );
        var resolver = new StepHeaderStatusResolver(modelService);
        var instance = CreateProjectInstance(("Start", new DateTime(2026, 04, 14, 9, 0, 0, DateTimeKind.Utc)));

        var status = resolver.Resolve(step, instance);

        Assert.NotNull(status);
        Assert.Equal(StepHeaderPillType.Info, status!.Type);
        Assert.Equal("Submitted on 14-04-2026", status.Label.En);
        Assert.Equal("Ingediend op 14-04-2026", status.Label.Nl);
    }

    [Fact]
    public async Task Factory_EmitsPendingHeaderStatusForSequentialParentWithoutCompleteVersion()
    {
        var modelService = CreateExampleModelService();
        var instance = CreateProjectInstance(("Start", new DateTime(2026, 04, 14, 9, 0, 0, DateTimeKind.Utc)));
        var repository = new Mock<IWorkflowInstanceRepository>();
        repository.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        repository
            .Setup(r => r.GetAllById(It.IsAny<string[]>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var factory = CreateWorkflowInstanceDtoFactory(modelService, repository);

        var dto = await factory.Create(instance, CancellationToken.None);
        var subject = dto.Steps.Single(s => s.Id == "Subject");

        Assert.NotNull(subject.HeaderStatus);
        Assert.Equal(StepHeaderPillType.Info, subject.HeaderStatus!.Type);
        Assert.Equal("Wait for approval", subject.HeaderStatus.Label.En);
        Assert.Null(subject.Versions);
    }

    private static WorkflowInstanceDtoFactory CreateWorkflowInstanceDtoFactory(
        ModelService modelService,
        Mock<IWorkflowInstanceRepository> repository)
    {
        var userService = new Mock<IUserService>();
        userService.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        userService.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var rightsService = new RightsService(modelService, userService.Object, repository.Object);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileKey"] = new string('a', 64)
            })
            .Build();

        var layoutResolver = new Mock<IMailLayoutResolver>();
        layoutResolver.Setup(r => r.Resolve(It.IsAny<string?>())).Returns(new Mock<IMailLayout>().Object);

        var instanceService = new InstanceService(
            repository.Object,
            modelService,
            userService.Object,
            rightsService,
            new MailBuilder(layoutResolver.Object, configuration)
        );
        var artifactTokenService = new Mock<IArtifactTokenService>();
        var submissionDtoFactory = new SubmissionDtoFactory(artifactTokenService.Object, modelService);
        var stepVersionService = new Mock<IStepVersionService>();
        stepVersionService
            .Setup(s => s.GetStepVersions(It.IsAny<WorkflowInstance>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var workflowInstanceService = new WorkflowInstanceService(
            modelService,
            repository.Object,
            Mock.Of<IInstanceJournalService>()
        );

        return new WorkflowInstanceDtoFactory(
            instanceService,
            modelService,
            submissionDtoFactory,
            repository.Object,
            rightsService,
            stepVersionService.Object,
            new StepHeaderStatusResolver(modelService),
            workflowInstanceService,
            NullLogger<WorkflowInstanceDtoFactory>.Instance
        );
    }

    private static ModelService CreateExampleModelService()
        => new(new ModelParser(new FileSystemProvider("../../../../Examples/Projects")));

    private static Step GetStep(ModelService modelService, string stepName)
        => modelService.WorkflowDefinitions["Project"].AllSteps.Single(s => s.Name == stepName);

    private static Step.StepHeaderStatusConfiguration GetHeaderStatusConfiguration(Step step, string eventId)
        => step.HeaderStatus!.Single(configuration => configuration.Event == eventId);

    private static WorkflowInstance CreateProjectInstance(params (string EventId, DateTime Date)[] events)
    {
        var builder = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Subject");

        foreach (var (eventId, date) in events)
            builder.WithEvent(eventId, date);

        return builder.Build();
    }
}