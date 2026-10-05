using MongoDB.Bson;
using Moq;
using UvA.Workflow.Jobs;
using UvA.Workflow.Notifications;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;

namespace UvA.Workflow.Tests;

public class EffectServiceTests : ControllerTestsBase
{
    [Fact]
    public async Task AddEvent_KeepsTimestampIncreasingWhenRecordingAnExistingEvent()
    {
        var previous = DateTime.UtcNow.AddHours(1);
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();
        instance.RecordEvent("Start", previous);

        await _effectService.AddEvent(instance, "Start", UnitTestsHelpers.AdminUser, _ct);

        Assert.Equal(previous.AddMilliseconds(1), instance.Events["Start"].Date);
        Assert.Equal(DateTimeKind.Utc, instance.Events["Start"].Date!.Value.Kind);
    }

    /// <summary>
    /// Verifies that MailLogEntry.ExecutedBy records whatever user the caller passes into
    /// EffectService.RunEffect — i.e. the real admin, not the impersonated target.
    /// The controller-level tests (ActionsControllerTests, SubmissionsControllerTests) verify
    /// that the correct user enters the pipeline; this test verifies the pipeline writes it
    /// to the mail log rather than reading from somewhere else.
    /// </summary>
    [Fact]
    public async Task MailLog_ExecutedBy_RecordsUserPassedIn_NotImpersonatedTarget()
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();

        MailLogEntry? capturedEntry = null;
        _mailLogRepositoryMock
            .Setup(r => r.Log(It.IsAny<MailLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<MailLogEntry, CancellationToken>((entry, _) => capturedEntry = entry)
            .Returns(Task.CompletedTask);

        var job = new Job
        {
            Id = ObjectId.GenerateNewId().ToString(),
            InstanceId = instance.Id,
            Input = new JobInput(new MailMessage("Test", "Test"))
        };
        var effect = new Effect { SendMail = new SendMessage { SendAutomatically = true } };
        var context = _modelService.CreateContext(instance);

        // Pass AdminUser — exactly what the fixed call sites now supply.
        // Passing ImpersonatedTarget here instead would write the wrong Id and fail both assertions.
        await _effectService.RunEffect(job, instance, effect, UnitTestsHelpers.AdminUser, context, _ct);

        Assert.NotNull(capturedEntry);
        Assert.Equal(UnitTestsHelpers.AdminUser.Id, capturedEntry.ExecutedBy);
        Assert.NotEqual(UnitTestsHelpers.ImpersonatedTarget.Id, capturedEntry.ExecutedBy);
    }
}