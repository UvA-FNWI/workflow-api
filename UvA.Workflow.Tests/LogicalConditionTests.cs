using UvA.Workflow.Tools;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests;

public class LogicalConditionTests
{
    [Theory]
    [InlineData("Submitted", "Approved", false, true)]
    [InlineData("Resubmitted", "Waived", false, true)]
    [InlineData("Submitted", null, false, false)]
    [InlineData(null, "Approved", false, false)]
    [InlineData("Submitted", "Approved", true, false)]
    public void NestedAndOrConditions_EvaluateNamedChildren(string? submission, string? decision, bool isRejected,
        bool expected)
    {
        var parser = new ModelParser(new DictionaryProvider(new()
        {
            ["Project/Entity.yaml"] = "name: Project\nsteps: [Review]",
            ["Project/Conditions/HasSubmitted.yaml"] = "event: Submitted",
            ["Project/Conditions/NotRejected.yaml"] = "event: Rejected\nnot: true",
            ["Project/Conditions/HasSubmission.yaml"] = """
                                                        or:
                                                          - HasSubmitted
                                                          - event: Resubmitted
                                                        """,
            ["Project/Steps/Review.yaml"] = """
                                            ends:
                                              and:
                                                - HasSubmission
                                                - or:
                                                    - event: Approved
                                                    - event: Waived
                                                - NotRejected
                                            """
        }));
        var condition = parser.WorkflowDefinitions["Project"].Steps.Single().Ends;
        var context = new ObjectContext(new Dictionary<Lookup, object?>());
        foreach (var eventId in new[] { submission, decision, isRejected ? "Rejected" : null }.OfType<string>())
        {
            context.Values[eventId + "Event"] = DateTime.UnixEpoch;
            context.Values[eventId + "EventActive"] = true;
        }

        Assert.Equal(expected, condition.IsMet(context));
    }

    [Fact]
    public void ModelParser_RejectsLegacyLogicalWrapper()
    {
        var exception = Assert.Throws<Exception>(() => new ModelParser(new DictionaryProvider(new()
        {
            ["Project/Entity.yaml"] = "name: Project\nsteps: [Review]",
            ["Project/Steps/Review.yaml"] = """
                                            ends:
                                              logical:
                                                operator: Or
                                                children:
                                                  - event: Approved
                                                  - event: Waived
                                            """
        })));

        Assert.Contains("Project/Steps/Review.yaml", exception.Message);
        Assert.Contains("logical", exception.Message);
    }
}