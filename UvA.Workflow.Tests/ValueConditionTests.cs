using MongoDB.Bson;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Tools;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests;

public class ValueConditionTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(" \t", true)]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "a" }, false)]
    [InlineData(new[] { 1 }, false)]
    [InlineData("a", false)]
    [InlineData(false, false)]
    [InlineData(0, false)]
    public void IsEmpty_HandlesNullStringsAndCollections(object? value, bool expected)
    {
        var context = new ObjectContext(new Dictionary<Lookup, object?> { ["Choices"] = value });
        var condition = new Value { Property = "Choices", IsEmpty = true };

        Assert.Equal(expected, condition.IsMet(context));

        condition.IsEmpty = false;
        Assert.Equal(!expected, condition.IsMet(context));
    }

    [Theory]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "None" }, true)]
    [InlineData(new[] { "None", "Alpha" }, true)]
    [InlineData(new[] { "None", "Alpha", "Beta" }, true)]
    [InlineData(new[] { "None", "Alpha", "Beta", "Gamma" }, false)]
    [InlineData(new[] { "Alpha", "Beta", "Gamma" }, false)]
    public void LessThanOrEqual_FixtureCountsSelectionsExceptNone(string[] selection, bool expected)
    {
        Assert.Equal(expected, EvaluateFixtureCondition("WithinLimit", selection));
    }

    [Theory]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "None" }, false)]
    [InlineData(new[] { "Alpha", "Beta" }, false)]
    public void IsEmpty_FixtureChecksMultiSelect(string[] selection, bool expected)
    {
        Assert.Equal(expected, EvaluateFixtureCondition("EmptySelection", selection));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void ExistingMilestonesCondition_PreservesEventAndValueChecks(bool uploaded, bool canBePublished,
        bool expected)
    {
        var model = new ModelService(UnitTestsHelpers.CreateModelParser());
        var condition = model.WorkflowDefinitions["Project"].AllSteps.Single(s => s.Name == "Publication").Condition!;
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Publication")
            .WithEvent("Upload", uploaded ? DateTime.UtcNow : null)
            .WithProperties(("CanBePublished", b => b.Value(canBePublished)))
            .Build();

        Assert.Equal(expected, condition.IsMet(model.CreateContext(instance)));
    }

    [Theory]
    [InlineData("Selection", "EmptySelection")] // plain property
    [InlineData("Selection", "WithinLimit")] // count(except(Selection, =None))
    [InlineData("Selection", "WithinSetLimit")] // except with ValueSets.…
    [InlineData("Limit", "WithinLimit")] // comparison operand
    [InlineData("Rows", "ScalarCodesWithinLimit")]
    [InlineData("Rows", "HasCodesOutsideSet")]
    [InlineData("Rows", "ScoreWithinLimit")]
    public void ExpressionProperty_RegistersDependentQuestion(string property, string dependent)
    {
        var definition = UnitTestsHelpers.CreateModelParser().WorkflowDefinitions["ExpressionConditions"];

        var source = definition.Properties.Single(p => p.Name == property);

        Assert.Contains(source.DependentQuestions, q => q.Name == dependent);
    }

    private static bool EvaluateFixtureCondition(string property, string[] selection)
    {
        var definition = UnitTestsHelpers.CreateModelParser().WorkflowDefinitions["ExpressionConditions"];
        var condition = definition.Properties.Single(p => p.Name == property).Condition!;
        var context = ObjectContext.Create(definition, new Dictionary<string, BsonValue>
        {
            ["Selection"] = new BsonArray(selection),
            ["Limit"] = 2
        });

        return condition.IsMet(context);
    }
}