using System.Text.Json;
using Moq;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Events;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class WorkflowDisplayContractTests : ControllerTestsBase
{
    [Fact]
    public async Task AvailabilityIncludesActiveAndCompletedStepsButExcludesFutureChildren()
    {
        var instance = new WorkflowInstanceBuilder().WithWorkflowDefinition("Project").WithCurrentStep("Start").Build();
        var repository = new Mock<IWorkflowInstanceRepository>();
        var factory = StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(_modelService, repository);

        var dto = await factory.Create(instance, CancellationToken.None);
        var subject = dto.Steps.Single(step => step.Id == "Subject");
        AssertAvailable(subject, true);
        AssertAvailable(subject.Children!.Single(step => step.Id == "Start"), true);
        AssertAvailable(subject.Children!.Single(step => step.Id == "SubjectFeedback"), false);
        AssertAvailable(dto.Steps.Single(step => step.Id == "Upload"), false);

        instance.Events["Start"] = new InstanceEvent { Id = "Start", Date = DateTime.UtcNow.AddMinutes(-1) };
        instance.CurrentStep = "SubjectFeedback";
        dto = await factory.Create(instance, CancellationToken.None);
        subject = dto.Steps.Single(step => step.Id == "Subject");
        AssertAvailable(subject.Children!.Single(step => step.Id == "Start"), true);
        AssertAvailable(subject.Children!.Single(step => step.Id == "SubjectFeedback"), true);
    }

    [Theory]
    [InlineData(StepMode.Alongside)]
    [InlineData(StepMode.Optional)]
    public async Task AvailabilityIncludesAlongsideWorkWhileAnotherStepIsCurrent(StepMode mode)
    {
        var definition = _modelService.WorkflowDefinitions["Project"];
        var first = new Step { Name = "First", Ends = new() { Event = "FirstDone" } };
        var alongside = new Step { Name = "Alongside", Mode = mode, Ends = new() { Event = "AlongsideDone" } };
        var current = new Step { Name = "Current", Ends = new() { Event = "CurrentDone" } };
        definition.Steps = [first, alongside, current];
        definition.AllSteps = [first, alongside, current];
        var instance = new WorkflowInstanceBuilder().WithWorkflowDefinition("Project").WithCurrentStep("Current")
            .WithEvent("FirstDone", DateTime.UtcNow.AddMinutes(-1)).Build();
        var factory =
            StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(_modelService,
                new Mock<IWorkflowInstanceRepository>());

        var dto = await factory.Create(instance, CancellationToken.None);

        AssertAvailable(dto.Steps.Single(step => step.Id == "Alongside"), true);
        AssertAvailable(dto.Steps.Single(step => step.Id == "Current"), true);
    }

    [Fact]
    public async Task InfoCardAppearsOnlyWhileItsCancellationConditionMatches()
    {
        const string studentEmail = "student@example.test";
        var model = new ModelService(new ModelParser(new DictionaryProvider(new()
        {
            ["Common/Conditions/Cancelled.yaml"] = "name: Cancelled\nevent: Cancel",
            ["Lifecycle/Entity.yaml"] = """
                                        name: Lifecycle
                                        stopWhen:
                                          logical:
                                            operator: Or
                                            children:
                                              - event: Cancel
                                              - event: Pause
                                        events:
                                          - name: Reopen
                                            suppresses: [Cancel]
                                        properties:
                                          - name: Student
                                            type: User
                                        infoCards:
                                          - name: GeneralInformation
                                            type: Text
                                            title: General information
                                            content: Workflow information
                                          - name: StopFollowUp
                                            type: Text
                                            title: Stop follow-up
                                            content:
                                              en: '[Email the student](mailto:{{ Student.Email }})'
                                              nl: '[Mail de student](mailto:{{ Student.Email }})'
                                            condition:
                                              name: Cancelled
                                        """
        })));
        var instance = new WorkflowInstanceBuilder().WithWorkflowDefinition("Lifecycle").WithCurrentStep("")
            .WithProperties(("Student", property => property.Person(email: studentEmail))).Build();
        var factory =
            StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(model, new Mock<IWorkflowInstanceRepository>());
        var pausedAt = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);

        await AssertVisibleCards("GeneralInformation");
        instance.RecordEvent("Pause", pausedAt);
        await AssertVisibleCards("GeneralInformation");
        instance.RecordEvent("Cancel", pausedAt.AddHours(1));
        var cards = await AssertVisibleCards("GeneralInformation", "StopFollowUp");
        var content = Assert.IsType<BilingualString>(cards.Single(card => card.Name == "StopFollowUp").Content);
        Assert.Equal($"[Email the student](mailto:{studentEmail})", content.En);
        Assert.Equal($"[Mail de student](mailto:{studentEmail})", content.Nl);
        instance.RecordEvent("Reopen", pausedAt.AddHours(2));
        await AssertVisibleCards("GeneralInformation");

        async Task<InfoCardDto[]> AssertVisibleCards(params string[] names)
        {
            var dto = await factory.Create(instance, CancellationToken.None);
            Assert.Equal(names, dto.InfoCards.Select(card => card.Name));
            Assert.Equal("Workflow information", dto.InfoCards[0].Content?.En);
            return dto.InfoCards;
        }
    }

    private static void AssertAvailable(StepDto step, bool expected)
        => Assert.Equal(expected, JsonSerializer.SerializeToElement(step).GetProperty("IsAvailable").GetBoolean());
}