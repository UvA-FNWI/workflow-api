using Moq;
using UvA.Workflow.Events;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.Versioning;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class CompoundResetParentStepTests
{
    private const string DraftAndChecklist = "and: [{ event: DraftSubmitted }, { event: ChecklistSubmitted }]";

    [Theory]
    [InlineData(DraftAndChecklist)]
    [InlineData("or: [{ and: [{ event: DraftSubmitted }, { event: ChecklistSubmitted }] }, { event: Waived }]")]
    public async Task Restart_WaitsForEntireFirstChildEnds(string ends)
    {
        var (model, instance, service, logs) = CreateWorkflow(ends);
        var definition = model.WorkflowDefinitions["Reset"];

        await Emit("DraftSubmitted");
        AssertStep("Draft");
        await Emit("ChecklistSubmitted");
        AssertStep("Review");
        await Emit("ReviewCompleted");
        AssertStep(null);

        foreach (var (first, second) in new[]
                 {
                     ("DraftSubmitted", "ChecklistSubmitted"),
                     ("ChecklistSubmitted", "DraftSubmitted")
                 })
        {
            await Emit("RevisionRequested");
            AssertStep("Draft");
            Assert.True(Active("ReviewCompleted"));
            Assert.True(Active("RevisionRequested"));

            await Emit(first);
            AssertStep("Draft");
            Assert.True(Active(first));
            Assert.False(Active(second));
            Assert.True(Active("ReviewCompleted"));
            Assert.True(Active("RevisionRequested"));

            await Emit(second);
            AssertStep("Review");
            Assert.True(Active(first));
            Assert.True(Active(second));
            Assert.False(Active("ReviewCompleted"));
            Assert.False(Active("RevisionRequested"));

            await Emit("ReviewCompleted");
            AssertStep(null);

            await Emit(first);
            AssertStep(null);
            Assert.True(Active("ReviewCompleted"));
        }

        Task Emit(string id) => service.UpdateEvent(instance, id, UnitTestsHelpers.AdminUser, CancellationToken.None);

        bool Active(string id) => EventSuppressionHelper.IsEventActive(id, instance, definition);

        void AssertStep(string? name)
        {
            Assert.Equal(name, model.FindOpenStep(instance)?.Name);
            var history = new WorkflowInstanceHistory(null, logs);
            Assert.Equal(name, history.GetInstanceAt(instance, logs[^1].Timestamp, model).CurrentStep);
        }
    }

    [Fact]
    public async Task ReemittingAnAlreadyMetCompletion_PreservesExistingReview()
    {
        var (model, instance, service, _) = CreateWorkflow("event: DraftSubmitted");
        instance.RecordEvent("DraftSubmitted");
        instance.RecordEvent("ReviewCompleted");
        Assert.Null(model.FindOpenStep(instance));

        await service.UpdateEvent(instance, "DraftSubmitted", UnitTestsHelpers.AdminUser, CancellationToken.None);

        Assert.Null(model.FindOpenStep(instance));
        Assert.True(EventSuppressionHelper.IsEventActive("ReviewCompleted", instance,
            model.WorkflowDefinitions["Reset"]));
    }

    [Fact]
    public async Task UndoingFinalPrerequisite_RestoresPreviousReviewAndItsVersion()
    {
        var (model, instance, service, logs) = CreateWorkflow(DraftAndChecklist);
        await Emit("DraftSubmitted");
        await Emit("ChecklistSubmitted");
        await Emit("ReviewCompleted");
        await Emit("RevisionRequested");
        await Emit("DraftSubmitted");
        const string completionOperation = "complete-checklist";
        await Emit("ChecklistSubmitted", completionOperation);

        var history = new WorkflowInstanceHistory(null, logs);
        var versionService = new StepVersionService(model);
        Assert.Equal(2, versionService.GetStepVersions(instance, history)["Review"].Current!.VersionNumber);

        var undoTimestamp = logs[^1].Timestamp.AddMilliseconds(1);
        logs.Add(new InstanceEventLogEntry
        {
            Operation = EventLogOperation.Undo, OperationId = completionOperation, Timestamp = undoTimestamp
        });
        var restored = history.GetInstanceAt(instance, undoTimestamp, model);

        var definition = model.WorkflowDefinitions["Reset"];
        Assert.Equal("Draft", restored.CurrentStep);
        Assert.True(EventSuppressionHelper.IsEventActive("ReviewCompleted", restored, definition));
        Assert.True(EventSuppressionHelper.IsEventActive("RevisionRequested", restored, definition));
        var review = versionService.GetStepVersions(restored, history)["Review"].Current!;
        Assert.Equal(1, review.VersionNumber);
        Assert.NotNull(review.FrozenAt);

        Task Emit(string id, string? operationId = null)
            => service.UpdateEvent(instance, id, UnitTestsHelpers.AdminUser, CancellationToken.None, operationId);
    }

    private static (ModelService Model, WorkflowInstance Instance, InstanceEventService Service,
        List<InstanceEventLogEntry> Logs) CreateWorkflow(string ends)
    {
        var model = new ModelService(new ModelParser(new DictionaryProvider(new()
        {
            ["Reset/Entity.yaml"] = "name: Reset\nsteps: [Parent]",
            ["Reset/Steps/Parent.yaml"] = "children: [Draft, Review]",
            ["Reset/Steps/Draft.yaml"] = $"ends:\n  {ends}",
            ["Reset/Steps/Review.yaml"] = """
                                          ends:
                                            event: ReviewCompleted
                                          events:
                                            - name: RevisionRequested
                                              resetParentStep: true
                                            # Review can also restart independently and has its own version history.
                                            - name: ReviewCompleted
                                              suppresses: [ReviewReset]
                                            - name: ReviewReset
                                              suppresses: [ReviewCompleted]
                                          """
        })));
        var definition = model.WorkflowDefinitions["Reset"];
        var instance = new WorkflowInstanceBuilder().With("Reset", "Draft").Build();
        instance.CreatedOn = DateTime.UtcNow.AddMinutes(-1);
        var logs = new List<InstanceEventLogEntry>();
        var repository = new Mock<IInstanceEventRepository>();
        repository.Setup(r => r.AddOrUpdateEvent(instance, It.IsAny<InstanceEvent>(),
                UnitTestsHelpers.AdminUser, It.IsAny<CancellationToken>(), It.IsAny<string?>(),
                It.IsAny<OperationMetadata?>()))
            .Callback<WorkflowInstance, InstanceEvent, User, CancellationToken, string?, OperationMetadata?>((_, ev, _,
                _, operationId, _) => Log(ev, operationId))
            .Returns(Task.CompletedTask);
        repository.Setup(r => r.RecordRestartEvent(instance, definition, It.IsAny<string>(),
                UnitTestsHelpers.AdminUser, It.IsAny<CancellationToken>(), It.IsAny<string?>(),
                It.IsAny<OperationMetadata?>()))
            .Callback<WorkflowInstance, WorkflowDefinition, string, User, CancellationToken, string?, OperationMetadata
                ?>((current, workflow, id, _, _, operationId, _) =>
            {
                var recorded = ParentStepPasses.RecordEvent(current, workflow, id);
                Log(recorded[0], operationId, recorded[1..]);
            })
            .Returns(Task.CompletedTask);
        var service = new InstanceEventService(repository.Object, null!, null!, model);
        return (model, instance, service, logs);

        void Log(InstanceEvent ev, string? operationId, InstanceEvent[]? boundaries = null)
            => logs.Add(new InstanceEventLogEntry
            {
                Id = logs.Count.ToString("D4"),
                EventId = ev.Id,
                EventDate = ev.Date,
                Timestamp = ev.Date!.Value,
                OperationId = operationId,
                PassBoundaries = boundaries
            });
    }
}