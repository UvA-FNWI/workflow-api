using MongoDB.Bson;
using UvA.Workflow.Expressions;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Tools;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class TimezoneTests
{
    [Fact]
    public void FutureUtcDateCondition_HasNotPassed()
    {
        var context = new ObjectContext(new Dictionary<Lookup, object?>
        {
            ["Future"] = DateTime.UtcNow.AddMinutes(30)
        });
        Assert.False(new UvA.Workflow.WorkflowModel.Conditions.Date { Source = "Future" }.IsMet(context));
    }

    [Fact]
    public void RecordEvent_NormalizesLocalInput_AndUsesCurrentUtcTime()
    {
        var instance = new WorkflowInstance { Events = [], Properties = [] };
        var local = DateTime.Now.AddMinutes(-60);
        var first = instance.RecordEvent("Local", local);
        Assert.Equal(DateTimeKind.Utc, first.Date!.Value.Kind);
        Assert.Equal(local.ToUniversalTime(), first.Date.Value);
        instance.RecordEvent("Utc", DateTime.UtcNow.AddMinutes(-30));
        var before = DateTime.UtcNow;

        var recorded = instance.RecordEvent("Next").Date!.Value;

        Assert.Equal(DateTimeKind.Utc, recorded.Kind);
        Assert.InRange(recorded, before, DateTime.UtcNow);
    }

    [Fact]
    public void ProjectedLastEvent_UsesLatestInstantAcrossAutumnClockChange()
    {
        var model = new ModelService(UnitTestsHelpers.CreateModelParser());
        var latest = new DateTime(2026, 10, 25, 1, 15, 0, DateTimeKind.Utc);
        var context = ObjectContext.Create(model.WorkflowDefinitions["Project"], new Dictionary<string, BsonValue>
        {
            ["Events"] = new BsonDocument
            {
                ["Start"] = new BsonDocument("Date", latest.AddMinutes(-30)),
                ["RejectSubject"] = new BsonDocument("Date", latest)
            }
        });

        Assert.Equal(latest, ((DateTime)context.Get("LastEvent")!).ToUniversalTime());
    }

    [Fact]
    public void CalendarDeadline_IsConsistentForFullAndProjectedInstancesAcrossDst()
    {
        var model = new ModelService(UnitTestsHelpers.CreateModelParser());
        var submitted = new DateTime(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);
        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project").WithCurrentStep("Start")
            .WithEvent("Start", submitted).Build();
        instance.CreatedOn = submitted;
        var full = model.CreateContext(instance);
        var projected = ObjectContext.Create(model.WorkflowDefinitions["Project"], new Dictionary<string, BsonValue>
        {
            ["CreateDate"] = new BsonDateTime(submitted),
            ["Events"] = new BsonDocument("Start", new BsonDocument("Date", submitted))
        });
        var deadline = new Deadline { Date = "addWeeks(StartEvent, 2)" };

        Assert.Equal(deadline.Evaluate(projected), deadline.Evaluate(full));
        Assert.Equal(new DateTimeOffset(submitted.ToLocalTime().AddDays(14)), deadline.Evaluate(full));
        Assert.Equal(projected.Get("CreateDate"), full.Get("CreateDate"));
        Assert.Equal(projected.Get("LastEvent"), full.Get("LastEvent"));
    }
}