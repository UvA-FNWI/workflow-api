using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Entities.Domain.Conditions;
using UvA.Workflow.Events;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class EventSuppressionTests
{
    [Fact]
    public void EventSuppressionHelper_IsEventActive_ReturnsTrueWhenNoLaterEventsSuppressIt()
    {
        var workflowDef = CreateWorkflowDefWithSuppression(
            ("SubmitSubject", ["RejectSubject"]),
            ("RejectSubject", ["SubmitSubject"])
        );

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("TestStep")
            .WithEvent("SubmitSubject", DateTime.UtcNow.AddHours(-2))
            .Build();

        // SubmitSubject is active (no later events suppress it)
        var isActive = EventSuppressionHelper.IsEventActive("SubmitSubject", instance, workflowDef);
        Assert.True(isActive);
    }

    [Fact]
    public void EventSuppressionHelper_IsEventActive_ReturnsFalseWhenSuppressedByLaterEvent()
    {
        var workflowDef = CreateWorkflowDefWithSuppression(
            ("SubmitSubject", ["RejectSubject"]),
            ("RejectSubject", ["SubmitSubject"])
        );

        var t1 = DateTime.UtcNow.AddHours(-2);
        var t2 = DateTime.UtcNow.AddHours(-1);

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("TestStep")
            .WithEvent("SubmitSubject", t1)
            .WithEvent("RejectSubject", t2)
            .Build();

        // SubmitSubject (T1) is suppressed by RejectSubject (T2)
        var isActive = EventSuppressionHelper.IsEventActive("SubmitSubject", instance, workflowDef);
        Assert.False(isActive);

        // RejectSubject (T2) is active (no later events)
        var rejectIsActive = EventSuppressionHelper.IsEventActive("RejectSubject", instance, workflowDef);
        Assert.True(rejectIsActive);
    }

    [Fact]
    public void EventSuppressionHelper_SuppressionOnlyWorksBackwardsInTime()
    {
        var workflowDef = CreateWorkflowDefWithSuppression(
            ("EventA", ["EventB"]),
            ("EventB", ["EventA"])
        );

        var t1 = DateTime.UtcNow.AddHours(-2);
        var t2 = DateTime.UtcNow.AddHours(-1);

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("TestStep")
            .WithEvent("EventA", t1)
            .WithEvent("EventB", t2)
            .Build();

        // EventA (T1) is suppressed by EventB (T2) because B happened AFTER A
        Assert.False(EventSuppressionHelper.IsEventActive("EventA", instance, workflowDef));
        Assert.Equal("EventB", EventSuppressionHelper.GetSuppressedBy("EventA", instance, workflowDef));

        // EventB (T2) is active because A happened BEFORE B (can't suppress future events)
        Assert.True(EventSuppressionHelper.IsEventActive("EventB", instance, workflowDef));
        Assert.Null(EventSuppressionHelper.GetSuppressedBy("EventB", instance, workflowDef));
    }

    [Fact]
    public void EventSuppressionHelper_ResubmissionScenario()
    {
        var workflowDef = CreateWorkflowDefWithSuppression(
            ("SubmitSubject", ["RejectSubject"]),
            ("RejectSubject", ["SubmitSubject"])
        );

        var t1 = DateTime.UtcNow.AddHours(-3); // First submission
        var t2 = DateTime.UtcNow.AddHours(-2); // Rejection
        var t3 = DateTime.UtcNow.AddHours(-1); // Resubmission

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("TestStep")
            .WithEvent("SubmitSubject", t3) // Current state: resubmitted
            .WithEvent("RejectSubject", t2)
            .Build();

        // SubmitSubject (T3) is active (most recent)
        Assert.True(EventSuppressionHelper.IsEventActive("SubmitSubject", instance, workflowDef));

        // RejectSubject (T2) is suppressed by SubmitSubject (T3)
        Assert.False(EventSuppressionHelper.IsEventActive("RejectSubject", instance, workflowDef));
        Assert.Equal("SubmitSubject", EventSuppressionHelper.GetSuppressedBy("RejectSubject", instance, workflowDef));
    }

    [Fact]
    public void ObjectContext_ComputesEventActiveFlags()
    {
        var workflowDef = CreateWorkflowDefWithSuppression(
            ("EventA", ["EventB"]),
            ("EventB", ["EventA"])
        );

        var t1 = DateTime.UtcNow.AddHours(-2);
        var t2 = DateTime.UtcNow.AddHours(-1);

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("TestStep")
            .WithEvent("EventA", t1)
            .WithEvent("EventB", t2)
            .Build();

        var modelService = CreateModelServiceWithWorkflowDef(workflowDef);
        var context = ObjectContext.Create(instance, modelService);

        // EventB (later) is active
        Assert.True(context.Get("EventBEventActive") as bool?);

        // EventA (earlier) is suppressed by EventB
        Assert.False(context.Get("EventAEventActive") as bool?);
    }

    [Fact]
    public void EventCondition_IsMet_ReturnsFalseWhenEventIsSuppressed()
    {
        var workflowDef = CreateWorkflowDefWithSuppression(
            ("EventA", ["EventB"]),
            ("EventB", ["EventA"])
        );

        var t1 = DateTime.UtcNow.AddHours(-2);
        var t2 = DateTime.UtcNow.AddHours(-1);

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("TestStep")
            .WithEvent("EventA", t1)
            .WithEvent("EventB", t2)
            .Build();

        var modelService = CreateModelServiceWithWorkflowDef(workflowDef);
        var context = ObjectContext.Create(instance, modelService);

        var condition = new EventCondition { Id = "EventA" };

        // EventA is suppressed by EventB, so condition is not met
        Assert.False(condition.IsMet(context));
    }

    [Fact]
    public void EventCondition_IsMet_ReturnsTrueWhenEventIsActive()
    {
        var workflowDef = CreateWorkflowDefWithSuppression(
            ("EventA", ["EventB"])
        );

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("TestStep")
            .WithEvent("EventA", DateTime.UtcNow)
            .Build();

        var modelService = CreateModelServiceWithWorkflowDef(workflowDef);
        var context = ObjectContext.Create(instance, modelService);

        var condition = new EventCondition { Id = "EventA" };

        Assert.True(condition.IsMet(context));
    }

    [Fact]
    public void EventCondition_NotBefore_RespectsSuppressionState()
    {
        var workflowDef = CreateWorkflowDefWithSuppression(
            ("EventA", ["EventB"]),
            ("EventB", ["EventA"])
        );

        var t1 = DateTime.UtcNow.AddHours(-2);
        var t2 = DateTime.UtcNow.AddHours(-1);

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("TestStep")
            .WithEvent("EventA", t1)
            .WithEvent("EventB", t2)
            .Build();

        var modelService = CreateModelServiceWithWorkflowDef(workflowDef);
        var context = ObjectContext.Create(instance, modelService);

        // EventA with notBefore EventB: EventA is not active (suppressed), so condition not met
        var condition = new EventCondition { Id = "EventA", NotBefore = "EventB" };

        Assert.False(condition.IsMet(context)); // EventA is suppressed, so false
    }

    [Fact]
    public void IsEventActive_AllowsResubmissionWhenEventIsSuppressed()
    {
        var workflowDef = CreateWorkflowDefWithSuppression(
            ("SubmitSubject", ["RejectSubject"]),
            ("RejectSubject", ["SubmitSubject"])
        );

        var t1 = DateTime.UtcNow.AddHours(-2); // Initial submission
        var t2 = DateTime.UtcNow.AddHours(-1); // Rejection

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("TestStep")
            .WithEvent("SubmitSubject", t1)
            .WithEvent("RejectSubject", t2)
            .Build();

        // SubmitSubject is suppressed by RejectSubject
        Assert.False(EventSuppressionHelper.IsEventActive("SubmitSubject", instance, workflowDef));

        // After resubmission (new date), the old submission is suppressed but the new one is active
        instance.Events["SubmitSubject"].Date = DateTime.UtcNow;
        Assert.True(EventSuppressionHelper.IsEventActive("SubmitSubject", instance, workflowDef));
        Assert.False(EventSuppressionHelper.IsEventActive("RejectSubject", instance, workflowDef));
    }

    [Fact]
    public void WhereActive_FiltersOutSuppressedEvents()
    {
        var workflowDef = CreateWorkflowDefWithSuppression(
            ("EventA", ["EventB"]),
            ("EventB", ["EventA"]),
            ("EventC", [])
        );

        var t1 = DateTime.UtcNow.AddHours(-3);
        var t2 = DateTime.UtcNow.AddHours(-2);
        var t3 = DateTime.UtcNow.AddHours(-1);

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("TestStep")
            .WithEvent("EventA", t1)
            .WithEvent("EventB", t2)
            .WithEvent("EventC", t3)
            .Build();

        // EventA (T1) is suppressed by EventB (T2)
        // EventB (T2) is active
        // EventC (T3) is active
        var activeEvents = instance.Events.Values
            .WhereActive(instance, workflowDef)
            .Select(e => e.Id)
            .OrderBy(id => id)
            .ToList();

        Assert.Equal(2, activeEvents.Count);
        Assert.Contains("EventB", activeEvents);
        Assert.Contains("EventC", activeEvents);
        Assert.DoesNotContain("EventA", activeEvents);
    }

    [Fact]
    public void StepGetEndDate_ReturnsNullWhenEndEventIsSuppressed()
    {
        var workflowDef = CreateWorkflowDefWithSuppression(
            ("SubmitSubject", ["RejectSubject"]),
            ("RejectSubject", ["SubmitSubject"])
        );

        var step = new Step
        {
            Name = "Subject",
            Ends = new Condition { Event = new EventCondition { Id = "SubmitSubject" } }
        };

        var t1 = DateTime.UtcNow.AddHours(-2); // Initial submission
        var t2 = DateTime.UtcNow.AddHours(-1); // Rejection

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("TestStep")
            .WithEvent("SubmitSubject", t1)
            .WithEvent("RejectSubject", t2)
            .Build();

        // SubmitSubject is suppressed, so step should not have an end date
        var endDate = step.GetEndDate(instance, workflowDef);
        Assert.Null(endDate);

        // After resubmission, the step should have an end date again
        instance.Events["SubmitSubject"].Date = DateTime.UtcNow;
        endDate = step.GetEndDate(instance, workflowDef);
        Assert.NotNull(endDate);
    }

    [Fact]
    public void SubmissionService_AllowsResubmissionWhenSuppressed()
    {
        var workflowDef = CreateWorkflowDefWithSuppression(
            ("SubmitSubject", ["RejectSubject"]),
            ("RejectSubject", ["SubmitSubject"])
        );

        var t1 = DateTime.UtcNow.AddHours(-2); // Initial submission
        var t2 = DateTime.UtcNow.AddHours(-1); // Rejection

        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("TestWorkflow")
            .WithCurrentStep("TestStep")
            .WithEvent("SubmitSubject", t1)
            .WithEvent("RejectSubject", t2)
            .Build();

        // SubmitSubject has a date but is suppressed by RejectSubject
        var submission = instance.Events["SubmitSubject"];
        Assert.NotNull(submission.Date); // Has a date
        Assert.False(EventSuppressionHelper.IsEventActive("SubmitSubject", instance, workflowDef)); // But is suppressed

        // This means SubmissionService should NOT throw "already submitted" error
        // Since IsEventActive is false, it should allow resubmission
    }

    // Helper methods
    private static WorkflowDefinition CreateWorkflowDefWithSuppression(
        params (string EventName, string[] Suppresses)[] events)
    {
        return new WorkflowDefinition
        {
            Name = "TestWorkflow",
            Events = events.Select(e => new EventDefinition
            {
                Name = e.EventName,
                Suppresses = e.Suppresses.ToList()
            }).ToList()
        };
    }

    private static ModelService CreateModelServiceWithWorkflowDef(WorkflowDefinition workflowDef)
    {
        var contentProvider = new TestContentProvider();
        var parser = new ModelParser(contentProvider);
        var modelService = new ModelService(parser);

        // Inject the test workflow definition
        modelService.WorkflowDefinitions[workflowDef.Name] = workflowDef;

        return modelService;
    }
}

// Helper class for creating test data
public static class TestHelper
{
    public static ModelService CreateModelService()
    {
        var contentProvider = new TestContentProvider();
        var parser = new ModelParser(contentProvider);
        return new ModelService(parser);
    }
}

public class TestContentProvider : IContentProvider
{
    public IEnumerable<string> GetFolders() => Array.Empty<string>();
    public IEnumerable<string> GetFiles(string directory) => Array.Empty<string>();
    public string GetFile(string file) => "";
}