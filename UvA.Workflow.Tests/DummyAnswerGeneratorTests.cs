using System.Text.Json;
using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests;

public class DummyAnswerGeneratorTests
{
    private static readonly QuestionStatus DefaultStatus = new(true, null, null);

    private static PropertyDefinition Question(string type, Condition? validation = null) => new()
    {
        Name = "TestProp",
        Type = type,
        Validation = validation
    };

    private static PropertyDefinition ChoiceQuestion(params string[] choices) => new()
    {
        Name = "TestProp",
        Type = "MyChoice",
        Values = choices.Select(c => new Choice { Name = c }).ToList()
    };

    private static QuestionStatus WithChoices(params string[] choices) =>
        new(true, null, choices);

    [Fact]
    public void Generate_String_ReturnsValue()
    {
        var result = new DummyAnswerGenerator().Generate(Question("String"), DefaultStatus);
        Assert.NotNull(result);
        Assert.Equal(JsonValueKind.String, result.Value.ValueKind);
    }


    [Fact]
    public void Generate_Int_ReturnsNumber()
    {
        var result = new DummyAnswerGenerator().Generate(Question("Int"), DefaultStatus);
        Assert.NotNull(result);
        Assert.Equal(JsonValueKind.Number, result.Value.ValueKind);
    }

    [Fact]
    public void Generate_Double_ReturnsNumber()
    {
        var result = new DummyAnswerGenerator().Generate(Question("Double"), DefaultStatus);
        Assert.NotNull(result);
        Assert.Equal(JsonValueKind.Number, result.Value.ValueKind);
    }

    [Fact]
    public void Generate_Boolean_ReturnsBool()
    {
        var result = new DummyAnswerGenerator().Generate(Question("Boolean"), DefaultStatus);
        Assert.NotNull(result);
        Assert.True(result.Value.ValueKind is JsonValueKind.True or JsonValueKind.False);
    }

    [Fact]
    public void Generate_DateTime_ReturnsIsoString()
    {
        var result = new DummyAnswerGenerator().Generate(Question("DateTime"), DefaultStatus);
        Assert.NotNull(result);
        Assert.Equal(JsonValueKind.String, result.Value.ValueKind);
        Assert.True(DateTime.TryParse(result.Value.GetString(), out _));
    }

    [Fact]
    public void Generate_UnknownType_ReturnsNull()
    {
        // File and User types are not generated
        var question = new PropertyDefinition
        {
            Name = "TestProp",
            Type = "File"
        };

        var result = new DummyAnswerGenerator().Generate(question, DefaultStatus);

        Assert.Null(result);
    }

    [Fact]
    public void Generate_ArrayWhereAllItemsAreNull_ReturnsNull()
    {
        var result = new DummyAnswerGenerator().Generate(Question("[File]"), DefaultStatus);
        Assert.Null(result);
    }

    // ── Choice ───────────────────────────────────────────────────────────────

    [Fact]
    public void Generate_Choice_ReturnsOneOfTheChoices()
    {
        var question = ChoiceQuestion("OptionA", "OptionB", "OptionC");
        var status = WithChoices("OptionA", "OptionB", "OptionC");

        var result = new DummyAnswerGenerator().Generate(question, status);

        Assert.NotNull(result);
        Assert.Contains(result.Value.GetString(), new[] { "OptionA", "OptionB", "OptionC" });
    }

    [Fact]
    public void Generate_Choice_NoChoicesInStatus_ReturnsNull()
    {
        var question = ChoiceQuestion("OptionA", "OptionB");

        var result = new DummyAnswerGenerator().Generate(question, DefaultStatus);

        Assert.Null(result);
    }

    // ── Array ────────────────────────────────────────────────────────────────

    [Fact]
    public void Generate_ArrayInt_ReturnsJsonArray()
    {
        var result = new DummyAnswerGenerator().Generate(Question("[Int]"), DefaultStatus);

        Assert.NotNull(result);
        Assert.Equal(JsonValueKind.Array, result.Value.ValueKind);
        Assert.All(result.Value.EnumerateArray(), el =>
            Assert.Equal(JsonValueKind.Number, el.ValueKind));
    }

    // ── Validation constraints applied ───────────────────────────────────────

    [Fact]
    public void Generate_Int_WithValidation_RespectsMinAndMax()
    {
        var validation = new Condition
        {
            Value = new Value { Property = "TestProp", GreaterThanOrEqual = "5", LessThan = "7" }
        };

        // Run many times to rule out lucky randoms
        for (var i = 0; i < 10; i++)
        {
            var result = new DummyAnswerGenerator().Generate(Question("Int", validation), DefaultStatus);
            var value = result!.Value.GetInt32();
            Assert.InRange(value, 5, 6);
        }
    }

    [Fact]
    public void Generate_Double_WithValidation_RespectsMinAndMax()
    {
        var validation = new Condition
        {
            Value = new Value { Property = "TestProp", GreaterThanOrEqual = "10", LessThan = "20" }
        };

        for (var i = 0; i < 50; i++)
        {
            var result = new DummyAnswerGenerator().Generate(Question("Double", validation), DefaultStatus);
            var value = result!.Value.GetDouble();
            Assert.InRange(value, 10.0, 20.0);
        }
    }

    [Fact]
    public void Generate_String_WithMaxLengthValidation_TruncatesString()
    {
        var validation = new Condition
        {
            Value = new Value { Property = "TestProp", MaxLength = 10 }
        };

        var result = new DummyAnswerGenerator().Generate(Question("String", validation), DefaultStatus);

        Assert.NotNull(result);
        Assert.True(result.Value.GetString()!.Length <= 10);
    }

    // ── Currency ─────────────────────────────────────────────────────────────

    [Fact]
    public void Generate_Currency_ReturnsObjectWithEurAndAmount()
    {
        var result = new DummyAnswerGenerator().Generate(Question("Currency"), DefaultStatus);

        Assert.NotNull(result);
        Assert.Equal(JsonValueKind.Object, result.Value.ValueKind);
        Assert.Equal("EUR", result.Value.GetProperty("currency").GetString());
        Assert.Equal(JsonValueKind.Number, result.Value.GetProperty("amount").ValueKind);
    }

    [Fact]
    public void Generate_Currency_WithValidation_RespectsRange()
    {
        var validation = new Condition
        {
            Value = new Value { Property = "TestProp", GreaterThanOrEqual = "50", LessThan = "52" }
        };

        for (var i = 0; i < 50; i++)
        {
            var result = new DummyAnswerGenerator().Generate(Question("Currency", validation), DefaultStatus);
            var amount = result!.Value.GetProperty("amount").GetInt32();
            Assert.InRange(amount, 50, 51);
        }
    }

    [Fact]
    public void TryGetLiteralNumber_IntegerString_ReturnsDouble()
    {
        var result = DummyAnswerGenerator.TryGetLiteralNumber("5");
        Assert.Equal(5.0, result);
    }

    [Fact]
    public void TryGetLiteralNumber_NegativeNumber_ReturnsDouble()
    {
        var result = DummyAnswerGenerator.TryGetLiteralNumber("-3");
        Assert.Equal(-3.0, result);
    }

    [Fact]
    public void TryGetLiteralNumber_NonLiteral_Identifier_ReturnsNull()
    {
        var result = DummyAnswerGenerator.TryGetLiteralNumber("OtherProperty");
        Assert.Null(result);
    }

    [Fact]
    public void TryGetLiteralNumber_StringLiteral_ReturnsNull()
    {
        var result = DummyAnswerGenerator.TryGetLiteralNumber("=hello");
        Assert.Null(result);
    }

    [Fact]
    public void TryGetLiteralNumber_Null_ReturnsNull()
    {
        var result = DummyAnswerGenerator.TryGetLiteralNumber(null);
        Assert.Null(result);
    }

    [Fact]
    public void ExtractFromValue_GreaterThan_SetsMinPlusOne()
    {
        var value = new Value { Property = "Score", GreaterThan = "0" };

        var result = DummyAnswerGenerator.ExtractFromValue(value);

        Assert.Equal(1.0, result.Min);
        Assert.Null(result.Max);
        Assert.Null(result.MaxLength);
    }

    [Fact]
    public void ExtractFromValue_GreaterThanOrEqual_SetsMinExactly()
    {
        var value = new Value { Property = "Score", GreaterThanOrEqual = "1" };

        var result = DummyAnswerGenerator.ExtractFromValue(value);

        Assert.Equal(1.0, result.Min);
    }

    [Fact]
    public void ExtractFromValue_LessThan_SetsMax()
    {
        var value = new Value { Property = "Score", LessThan = "100" };

        var result = DummyAnswerGenerator.ExtractFromValue(value);

        Assert.Equal(100.0, result.Max);
        Assert.Null(result.Min);
    }

    [Fact]
    public void ExtractFromValue_GreaterThanAndLessThan_SetsBothBounds()
    {
        var value = new Value { Property = "Score", GreaterThan = "0", LessThan = "10" };

        var result = DummyAnswerGenerator.ExtractFromValue(value);

        Assert.Equal(1.0, result.Min);
        Assert.Equal(10.0, result.Max);
    }

    [Fact]
    public void ExtractFromValue_MaxLength_SetsMaxLength()
    {
        var value = new Value { Property = "Name", MaxLength = 50 };

        var result = DummyAnswerGenerator.ExtractFromValue(value);

        Assert.Equal(50, result.MaxLength);
        Assert.Null(result.Min);
        Assert.Null(result.Max);
    }

    [Fact]
    public void ExtractFromValue_NonLiteralGreaterThan_MinIsNull()
    {
        // References another property — can't determine a literal bound
        var value = new Value { Property = "Score", GreaterThan = "MinScore" };

        var result = DummyAnswerGenerator.ExtractFromValue(value);

        Assert.Null(result.Min);
    }

    [Fact]
    public void ExtractConstraints_Null_ReturnsNone()
    {
        var result = DummyAnswerGenerator.ExtractConstraints(null);

        Assert.Equal(DummyAnswerGenerator.ValidationConstraints.None, result);
    }


    [Fact]
    public void ExtractConstraints_NotCondition_ReturnsNone()
    {
        var condition = new Condition
        {
            Not = true,
            Value = new Value { Property = "Score", LessThan = "100" }
        };

        var result = DummyAnswerGenerator.ExtractConstraints(condition);

        Assert.Equal(DummyAnswerGenerator.ValidationConstraints.None, result);
    }

    [Fact]
    public void ExtractConstraints_LogicalOr_ReturnsNone()
    {
        var condition = new Condition
        {
            Logical = new Logical
            {
                Operator = LogicalOperator.Or,
                Children =
                [
                    new Condition { Value = new Value { Property = "Score", GreaterThanOrEqual = "1" } },
                    new Condition { Value = new Value { Property = "Score", LessThan = "10" } }
                ]
            }
        };

        var result = DummyAnswerGenerator.ExtractConstraints(condition);

        Assert.Equal(DummyAnswerGenerator.ValidationConstraints.None, result);
    }

    [Fact]
    public void ExtractFromLogical_And_IntersectsBounds()
    {
        var logical = new Logical
        {
            Operator = LogicalOperator.And,
            Children =
            [
                new Condition { Value = new Value { Property = "Score", GreaterThanOrEqual = "5" } },
                new Condition { Value = new Value { Property = "Score", LessThan = "20" } }
            ]
        };

        var result = DummyAnswerGenerator.ExtractFromLogical(logical);

        Assert.Equal(5.0, result.Min);
        Assert.Equal(20.0, result.Max);
    }

    [Fact]
    public void ExtractFromLogical_And_TakesStrictestMin()
    {
        var logical = new Logical
        {
            Operator = LogicalOperator.And,
            Children =
            [
                new Condition { Value = new Value { Property = "Score", GreaterThanOrEqual = "3" } },
                new Condition { Value = new Value { Property = "Score", GreaterThanOrEqual = "7" } }
            ]
        };

        var result = DummyAnswerGenerator.ExtractFromLogical(logical);

        Assert.Equal(7.0, result.Min);
    }

    [Fact]
    public void ExtractFromLogical_And_TakesStrictestMax()
    {
        var logical = new Logical
        {
            Operator = LogicalOperator.And,
            Children =
            [
                new Condition { Value = new Value { Property = "Score", LessThan = "50" } },
                new Condition { Value = new Value { Property = "Score", LessThan = "20" } }
            ]
        };

        var result = DummyAnswerGenerator.ExtractFromLogical(logical);

        Assert.Equal(20.0, result.Max);
    }

    [Fact]
    public void ExtractFromLogical_And_MaxLength_TakesSmallest()
    {
        var logical = new Logical
        {
            Operator = LogicalOperator.And,
            Children =
            [
                new Condition { Value = new Value { Property = "Name", MaxLength = 100 } },
                new Condition { Value = new Value { Property = "Name", MaxLength = 50 } }
            ]
        };

        var result = DummyAnswerGenerator.ExtractFromLogical(logical);

        Assert.Equal(50, result.MaxLength);
    }
}