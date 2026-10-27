using MongoDB.Bson;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests;

/// <summary>
/// select / flatten / unique / min / max on an embedded list, driven by the ExpressionConditions fixture YAML
/// </summary>
public class SelectExpressionTests
{
    [Theory]
    [InlineData(2, true)]
    [InlineData(1, false)]
    public void ScalarField_CountsUniqueValuesCaseInsensitively(int limit, bool expected)
    {
        var rows = new[]
        {
            new BsonDocument("Code", "AA"),
            new BsonDocument("Code", "aa"),
            new BsonDocument("Code", "BB")
        };

        Assert.Equal(expected, Evaluate("ScalarCodesWithinLimit", limit, rows));
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(2, false)]
    public void ArrayField_FlattensAndCountsUniqueValues(int limit, bool expected)
    {
        var rows = new[]
        {
            new BsonDocument("Codes", new BsonArray { "AA", "BB" }),
            new BsonDocument("Codes", new BsonArray { "bb", "CC" })
        };

        Assert.Equal(expected, Evaluate("ArrayCodesWithinLimit", limit, rows));
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(1, false)]
    public void MixedRows_SkipMissingNullAndEmptyValues(int limit, bool expected)
    {
        var rows = new[]
        {
            new BsonDocument("Codes", new BsonArray { "AA" }),
            new BsonDocument("Code", "ZZ"),
            new BsonDocument("Codes", new BsonArray()),
            new BsonDocument("Codes", BsonNull.Value),
            new BsonDocument("Codes", new BsonArray { "BB" })
        };

        Assert.Equal(expected, Evaluate("ArrayCodesWithinLimit", limit, rows));
    }

    [Theory]
    [InlineData(new[] { "AA", "ZZ" }, true)]
    [InlineData(new[] { "AA", "BB" }, false)]
    public void ExceptValueSet_DetectsCodesOutsideTheSet(string[] codes, bool expected)
    {
        var rows = new[] { new BsonDocument("Codes", new BsonArray(codes)) };

        Assert.Equal(expected, Evaluate("HasCodesOutsideSet", 0, rows));
    }

    [Theory]
    [InlineData(9, true)]
    [InlineData(8, false)]
    public void Max_ComparesHighestScore(int limit, bool expected)
    {
        var rows = new[]
        {
            new BsonDocument("Score", 4),
            new BsonDocument("Code", "AA"),
            new BsonDocument("Score", 9)
        };

        Assert.Equal(expected, Evaluate("ScoreWithinLimit", limit, rows));
    }

    [Fact]
    public void EmptyParentList_GivesEmptyResult()
    {
        Assert.True(Evaluate("ArrayCodesWithinLimit", 0));
        Assert.True(Evaluate("ScalarCodesWithinLimit", 0));
        Assert.False(Evaluate("HasCodesOutsideSet", 0));
        Assert.False(Evaluate("ScoreWithinLimit", 0));
    }

    [Fact]
    public void MissingParentList_GivesEmptyResult()
    {
        var definition = UnitTestsHelpers.CreateModelParser().WorkflowDefinitions["ExpressionConditions"];
        var condition = definition.Properties.Single(p => p.Name == "ArrayCodesWithinLimit").Condition!;
        var context = ObjectContext.Create(definition, new Dictionary<string, BsonValue> { ["Limit"] = 0 });

        Assert.True(condition.IsMet(context));
    }

    private static bool Evaluate(string property, int limit, params BsonDocument[] rows)
    {
        var definition = UnitTestsHelpers.CreateModelParser().WorkflowDefinitions["ExpressionConditions"];
        var condition = definition.Properties.Single(p => p.Name == property).Condition!;
        var context = ObjectContext.Create(definition, new Dictionary<string, BsonValue>
        {
            ["Rows"] = new BsonArray(rows),
            ["Limit"] = limit
        });

        return condition.IsMet(context);
    }
}