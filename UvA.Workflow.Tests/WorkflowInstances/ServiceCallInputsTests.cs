using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Services;
using UvA.Workflow.WorkflowInstances.ServiceCalls;

namespace UvA.Workflow.Tests.WorkflowInstances;

public class ServiceCallInputsTests
{
    [Fact]
    public void GetMissingInputs_IgnoresLiteralInputs()
    {
        var inputs = new ServiceCallInputs(new Dictionary<string, string>
        {
            ["grade"] = "9",
            ["passed"] = "true",
            ["comment"] = "=great"
        });
        var context = new ObjectContext(new Dictionary<Lookup, object?>());

        var missingInputs = inputs.GetMissingInputs(context);
        var requestContext = inputs.CreateRequestContext(context);

        Assert.Empty(missingInputs);
        Assert.Equal(9, requestContext.Get("grade"));
        Assert.Equal(true, requestContext.Get("passed"));
        Assert.Equal("great", requestContext.Get("comment"));
    }

    [Fact]
    public void GetMissingInputs_OnlyReturnsMissingReferences()
    {
        var inputs = new ServiceCallInputs(new Dictionary<string, string>
        {
            ["grade"] = "FinalGrade",
            ["fallback"] = "9",
            ["deadline"] = "addDays(StartDate, 1)"
        });
        var context = new ObjectContext(new Dictionary<Lookup, object?>
        {
            ["StartDate"] = new DateTime(2026, 4, 8)
        });

        var missingInputs = inputs.GetMissingInputs(context);

        Assert.Equal(["grade<-FinalGrade"], missingInputs);
    }

    [Fact]
    public void Lookups_AreDistinctAcrossInputs()
    {
        var inputs = new ServiceCallInputs(new Dictionary<string, string>
        {
            ["deadline"] = "addDays(StartDate, 1)",
            ["reminder"] = "addDays(StartDate, 2)"
        });

        Assert.Equal(["StartDate"], inputs.Lookups.Select(lookup => lookup.ToString()));
    }
}