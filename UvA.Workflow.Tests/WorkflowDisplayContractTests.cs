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
        var feedback = subject.Children!.Single(step => step.Id == "SubjectFeedback");
        AssertAvailable(feedback, false);
        Assert.False(feedback.IsActive);
        AssertAvailable(dto.Steps.Single(step => step.Id == "Upload"), false);

        instance.Events["Start"] = new InstanceEvent { Id = "Start", Date = DateTime.UtcNow.AddMinutes(-1) };
        instance.CurrentStep = "SubjectFeedback";
        dto = await factory.Create(instance, CancellationToken.None);
        subject = dto.Steps.Single(step => step.Id == "Subject");
        var start = subject.Children!.Single(step => step.Id == "Start");
        AssertAvailable(start, true);
        Assert.False(start.IsActive);
        AssertAvailable(subject.Children!.Single(step => step.Id == "SubjectFeedback"), true);
    }

    [Theory]
    [InlineData(StepMode.Alongside)]
    [InlineData(StepMode.Optional)]
    public async Task AlongsideChildIsActiveWhileAnotherStepIsCurrent(StepMode mode)
    {
        var definition = _modelService.WorkflowDefinitions["Project"];
        var first = new Step { Name = "First", Ends = new() { Event = "FirstDone" } };
        var alongside = new Step { Name = "Alongside", Mode = mode };
        var child = new Step
        {
            Name = "Child", ParentStep = alongside, Ends = new() { Event = "ChildDone" },
            Deadline = new() { Date = "=2999-01-01" }
        };
        alongside.Children = [child];
        var current = new Step { Name = "Current", Ends = new() { Event = "CurrentDone" } };
        definition.Steps = [first, alongside, current];
        definition.AllSteps = [first, alongside, child, current];
        var instance = new WorkflowInstanceBuilder().WithWorkflowDefinition("Project").WithCurrentStep("Current")
            .WithEvent("FirstDone", DateTime.UtcNow.AddMinutes(-1)).Build();
        var factory =
            StepHeaderStatusTests.CreateWorkflowInstanceDtoFactory(_modelService,
                new Mock<IWorkflowInstanceRepository>());

        var dto = await factory.Create(instance, CancellationToken.None);

        Assert.Equal("Current", dto.CurrentStep);
        var alongsideDto = dto.Steps.Single(step => step.Id == "Alongside");
        AssertAvailable(alongsideDto, true);
        var childDto = Assert.Single(alongsideDto.Children!);
        var json = JsonSerializer.SerializeToElement(childDto, JsonSerializerOptions.Web);
        Assert.True(json.GetProperty("isActive").GetBoolean());
        Assert.NotNull(childDto.Deadline?.Date);
        AssertAvailable(dto.Steps.Single(step => step.Id == "Current"), true);
    }

    private static void AssertAvailable(StepDto step, bool expected)
        => Assert.Equal(expected, JsonSerializer.SerializeToElement(step).GetProperty("IsAvailable").GetBoolean());
}