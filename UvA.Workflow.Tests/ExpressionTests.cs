using UvA.Workflow.Expressions;
using UvA.Workflow.Tools;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests;

public class ExpressionTests
{
    [Theory]
    [InlineData("=Bachelor's thesis", "Bachelor's thesis")]
    [InlineData("if(true, =Bachelor's thesis, =Other)", "Bachelor's thesis")]
    [InlineData("=A 6\" screen", "A 6\" screen")]
    [InlineData("=\"One, two\"", "\"One, two\"")]
    [InlineData("template(\"Bachelor's ({{ Title }})\")", "Bachelor's (thesis)")]
    public void Parse_PreservesQuotesWithinLiterals(string expression, string expected)
    {
        var context = new ObjectContext(new Dictionary<Lookup, object?> { ["Title"] = "thesis" });

        Assert.Equal(expected, ExpressionParser.Parse(expression).Execute(context));
    }

    [Fact]
    public void Parse_RejectsUnclosedQuotedTemplate()
        => Assert.Throws<ArgumentException>(() => ExpressionParser.Parse("template(\"Unclosed)"));

    [Theory]
    [InlineData(null, 0)]
    [InlineData(new string[0], 0)]
    [InlineData(new[] { "a", "b" }, 2)]
    [InlineData(new[] { 1, 2, 3 }, 3)]
    public void Count_ReturnsCollectionSize(object? values, int expected)
    {
        var exp = ExpressionParser.Parse("count(Choices)");
        var context = new ObjectContext(new Dictionary<Lookup, object?> { ["Choices"] = values });

        Assert.Equal(expected, exp.Execute(context));
        Assert.Equal([new PropertyLookup("Choices")], exp.Properties);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Alpha")]
    [InlineData(42)]
    [InlineData(42.5)]
    public void Count_RejectsStringsAndNumericScalars(object value)
    {
        var exp = ExpressionParser.Parse("count(Choices)");
        var context = new ObjectContext(new Dictionary<Lookup, object?> { ["Choices"] = value });

        var error = Assert.Throws<ArgumentException>(() => exp.Execute(context));

        Assert.Equal("count expects a collection", error.Message);
    }

    [Theory]
    [InlineData("except(Choices, =ab)")]
    [InlineData("except(Choices, Excluded)")]
    public void Except_RemovesAnItemOrACollection(string expression)
    {
        var exp = ExpressionParser.Parse(expression);
        var choices = new[] { "ab", "a", "b", "ab" };
        var context = new ObjectContext(new Dictionary<Lookup, object?>
        {
            ["Choices"] = choices,
            ["Excluded"] = new[] { "ab", "missing" }
        });

        Assert.Equal(new object[] { "a", "b" }, Assert.IsType<object[]>(exp.Execute(context)));
        Assert.Equal(new[] { "ab", "a", "b", "ab" }, choices);
    }

    [Fact]
    public void Except_SupportsValueTypeCollectionsAndNestedCount()
    {
        var exp = ExpressionParser.Parse("count(except(Choices, Excluded))");
        var context = new ObjectContext(new Dictionary<Lookup, object?>
        {
            ["Choices"] = new[] { 1, 2, 3 },
            ["Excluded"] = new[] { 1, 3 }
        });

        Assert.Equal(1, exp.Execute(context));
        Assert.Equal([new PropertyLookup("Choices"), new PropertyLookup("Excluded")], exp.Properties);
    }

    [Fact]
    public void Except_RemovesAllValuesInASet()
    {
        var exp = ExpressionParser.Parse("except(Choices, Excluded)");
        var context = new ObjectContext(new Dictionary<Lookup, object?>
        {
            ["Choices"] = new[] { "Alpha", "Beta", "Gamma" },
            ["Excluded"] = new HashSet<string> { "Alpha", "Gamma" }
        });

        Assert.Equal(new object[] { "Beta" }, Assert.IsType<object[]>(exp.Execute(context)));
    }

    [Theory]
    [InlineData("except(Choices, =None)")]
    [InlineData("except(Choices, Excluded)")]
    public void Except_KeepsDuplicatesThatAreNotExcluded(string expression)
    {
        var context = new ObjectContext(new Dictionary<Lookup, object?>
        {
            ["Choices"] = new[] { "Alpha", "None", "Beta", "Alpha" },
            ["Excluded"] = new[] { "None" }
        });

        Assert.Equal(new object[] { "Alpha", "Beta", "Alpha" },
            Assert.IsType<object[]>(ExpressionParser.Parse(expression).Execute(context)));
    }

    [Fact]
    public void Except_CountsRemainingSelectedRows()
    {
        var exp = ExpressionParser.Parse("count(except(select(Rows, =Code), =None))");
        var rows = new[]
        {
            new Dictionary<string, object> { ["Code"] = "Alpha" },
            new Dictionary<string, object> { ["Code"] = "Alpha" },
            new Dictionary<string, object> { ["Code"] = "None" }
        };

        Assert.Equal(2, exp.Execute(RowsContext(rows)));
    }

    [Fact]
    public void Except_NullCollection_ReturnsEmptyCollection()
    {
        var exp = ExpressionParser.Parse("except(Missing, =ab)");

        Assert.Empty(Assert.IsType<object[]>(exp.Execute(new ObjectContext([]))));
    }

    [Fact]
    public void ValueSets_ResolveFromSeparateCollection()
    {
        var context = new ObjectContext(
            new Dictionary<Lookup, object?> { ["Selection"] = new[] { "None", "Alpha", "Beta" } },
            new Dictionary<string, string[]> { ["Excluded"] = ["None", "Beta"] });

        Assert.Equal(new object[] { "Alpha" },
            Assert.IsType<object[]>(ExpressionParser.Parse("except(Selection, ValueSets.Excluded)").Execute(context)));
    }

    [Fact]
    public void ValueSets_DoNotClashWithValuesOfTheSameName()
    {
        // e.g. an access effect adds its output as "Access" while a value set is also called Access
        var context = new ObjectContext(
            new Dictionary<Lookup, object?> { ["Access"] = "effect output" },
            new Dictionary<string, string[]> { ["Access"] = ["Read", "Write"] });

        Assert.Equal("effect output", ExpressionParser.Parse("Access").Execute(context));
        Assert.Equal(new[] { "Read", "Write" }, ExpressionParser.Parse("ValueSets.Access").Execute(context));
    }

    [Fact]
    public void ValueSets_AreNotReportedAsPropertiesToLoad()
    {
        Assert.Equal([new PropertyLookup("Selection")],
            ExpressionParser.Parse("count(except(Selection, ValueSets.Excluded))").Properties);
        Assert.Empty(ExpressionParser.Parse("ValueSets.Excluded").Properties);
    }

    [Fact]
    public void ValueSets_UnknownName_ReturnsNull()
    {
        var context = new ObjectContext([], new Dictionary<string, string[]> { ["Excluded"] = ["None"] });

        Assert.Null(ExpressionParser.Parse("ValueSets.Missing").Execute(context));
        Assert.Null(ExpressionParser.Parse("ValueSets.Excluded").Execute(new ObjectContext([])));
    }

    private static ObjectContext RowsContext(object? rows)
        => new(new Dictionary<Lookup, object?> { ["Rows"] = rows });

    [Fact]
    public void Select_ScalarField_ReturnsValuePerRow()
    {
        var exp = ExpressionParser.Parse("select(Rows, =Code)");
        var rows = new[]
        {
            new Dictionary<string, object> { ["Code"] = "NL" },
            new Dictionary<string, object> { ["Code"] = "FR" }
        };

        Assert.Equal(new object[] { "NL", "FR" }, Assert.IsType<object[]>(exp.Execute(RowsContext(rows))));
        Assert.Equal([new PropertyLookup("Rows.Code")], exp.Properties);
    }

    [Fact]
    public void Select_ArrayField_KeepsArraysForFlatten()
    {
        var exp = ExpressionParser.Parse("select(Rows, =Codes)");
        var rows = new[]
        {
            new Dictionary<string, object> { ["Codes"] = new List<object> { "NL", "BE" } },
            new Dictionary<string, object> { ["Codes"] = new List<object> { "FR" } }
        };

        var result = Assert.IsType<object[]>(exp.Execute(RowsContext(rows)));

        Assert.Equal(2, result.Length);
        Assert.Equal(new List<object> { "NL", "BE" }, result[0]);
        Assert.Equal(new List<object> { "FR" }, result[1]);
    }

    [Fact]
    public void Select_MixedRows_SkipsNullAndEmptyValues()
    {
        var exp = ExpressionParser.Parse("flatten(select(Rows, =Codes))");
        var rows = new[]
        {
            new Dictionary<string, object> { ["Codes"] = new List<object> { "NL", "BE" } },
            new Dictionary<string, object> { ["Codes"] = "FR" },
            new Dictionary<string, object> { ["Codes"] = new List<object>() },
            new Dictionary<string, object> { ["Codes"] = " " },
            new Dictionary<string, object> { ["Other"] = "DE" },
            null
        };

        Assert.Equal(new object[] { "NL", "BE", "FR" }, Assert.IsType<object[]>(exp.Execute(RowsContext(rows))));
    }

    [Fact]
    public void Select_LookupKeyedRows_AreSupported()
    {
        var exp = ExpressionParser.Parse("select(Rows, =Code)");
        var rows = new[] { new Dictionary<Lookup, object> { ["Code"] = "NL" } };

        Assert.Equal(new object[] { "NL" }, Assert.IsType<object[]>(exp.Execute(RowsContext(rows))));
    }

    [Fact]
    public void Select_NullOrEmptyList_ReturnsEmptyCollection()
    {
        var exp = ExpressionParser.Parse("select(Rows, =Code)");

        Assert.Empty(Assert.IsType<object[]>(exp.Execute(RowsContext(null))));
        Assert.Empty(Assert.IsType<object[]>(exp.Execute(RowsContext(Array.Empty<Dictionary<string, object>>()))));
    }

    [Fact]
    public void Select_FieldWithoutEqualsSign_Throws()
    {
        // Without '=', Code is looked up as a property, which doesn't exist → null field name
        var exp = ExpressionParser.Parse("select(Rows, Code)");
        var rows = new[] { new Dictionary<string, object> { ["Code"] = "NL" } };

        var error = Assert.Throws<ArgumentException>(() => exp.Execute(RowsContext(rows)));

        Assert.Contains("field name", error.Message);
    }

    [Fact]
    public void Flatten_FlattensOneLevelAndKeepsScalars()
    {
        var exp = ExpressionParser.Parse("flatten(Values)");
        var context = new ObjectContext(new Dictionary<Lookup, object?>
        {
            ["Values"] = new object[] { new[] { "a", "b" }, "c", new List<object> { "d", new[] { "e" } } }
        });

        var result = Assert.IsType<object[]>(exp.Execute(context));

        Assert.Equal(5, result.Length);
        Assert.Equal(new object[] { "a", "b", "c", "d" }, result[..4]);
        Assert.Equal(new[] { "e" }, result[4]);
    }

    [Fact]
    public void Flatten_NullOrEmpty_ReturnsEmptyCollection()
    {
        var exp = ExpressionParser.Parse("flatten(Values)");

        Assert.Empty(Assert.IsType<object[]>(exp.Execute(new ObjectContext([]))));
        Assert.Empty(Assert.IsType<object[]>(exp.Execute(new ObjectContext(
            new Dictionary<Lookup, object?> { ["Values"] = Array.Empty<object>() }))));
    }

    [Fact]
    public void Unique_IsCaseInsensitiveAndKeepsFirstOccurrence()
    {
        var exp = ExpressionParser.Parse("unique(Values)");
        var context = new ObjectContext(new Dictionary<Lookup, object?>
        {
            ["Values"] = new object[] { "NL", "fr", "nl", "BE", "FR", 1, 1 }
        });

        Assert.Equal(new object[] { "NL", "fr", "BE", 1 }, Assert.IsType<object[]>(exp.Execute(context)));
    }

    [Fact]
    public void Unique_NullCollection_ReturnsEmptyCollection()
    {
        var exp = ExpressionParser.Parse("unique(Values)");

        Assert.Empty(Assert.IsType<object[]>(exp.Execute(new ObjectContext([]))));
    }

    [Fact]
    public void UniqueFlatten_ComposeAndCount()
    {
        var exp = ExpressionParser.Parse("count(unique(flatten(Values)))");
        var context = new ObjectContext(new Dictionary<Lookup, object?>
        {
            ["Values"] = new object[] { new[] { "NL" }, new[] { "fr", "BE" }, new[] { "FR", "nl" } }
        });

        Assert.Equal(3, exp.Execute(context));
        Assert.Equal([new PropertyLookup("Values")], exp.Properties);
    }

    [Fact]
    public void MinMax_ReturnSmallestAndLargestNumber()
    {
        var context = new ObjectContext(new Dictionary<Lookup, object?> { ["Values"] = new object[] { 3, 1, 7, 2 } });

        Assert.Equal(1, ExpressionParser.Parse("min(Values)").Execute(context));
        Assert.Equal(7, ExpressionParser.Parse("max(Values)").Execute(context));
    }

    [Fact]
    public void MinMax_MixedNumericTypes_ReturnOriginalValue()
    {
        var context = new ObjectContext(new Dictionary<Lookup, object?>
        {
            ["Values"] = new object[] { 2, 2.5, 10L, 0.5m }
        });

        Assert.Equal(0.5m, ExpressionParser.Parse("min(Values)").Execute(context));
        Assert.Equal(10L, ExpressionParser.Parse("max(Values)").Execute(context));
    }

    [Fact]
    public void MinMax_NullOrEmpty_ReturnNull()
    {
        var empty = new ObjectContext(new Dictionary<Lookup, object?> { ["Values"] = Array.Empty<int>() });

        Assert.Null(ExpressionParser.Parse("min(Values)").Execute(empty));
        Assert.Null(ExpressionParser.Parse("max(Values)").Execute(empty));
        Assert.Null(ExpressionParser.Parse("max(Values)").Execute(new ObjectContext([])));
    }

    [Theory]
    [InlineData("min")]
    [InlineData("max")]
    public void MinMax_NonNumericValues_Throw(string function)
    {
        var exp = ExpressionParser.Parse($"{function}(Values)");
        var context = new ObjectContext(new Dictionary<Lookup, object?> { ["Values"] = new object[] { 1, "2" } });

        var error = Assert.Throws<ArgumentException>(() => exp.Execute(context));

        Assert.Equal($"{function} expects a list of numbers", error.Message);
    }

    [Fact]
    public void Max_ComposesWithSelect()
    {
        var exp = ExpressionParser.Parse("max(select(Rows, =Score))");
        var rows = new[]
        {
            new Dictionary<string, object> { ["Score"] = 4 },
            new Dictionary<string, object> { ["Other"] = "x" },
            new Dictionary<string, object> { ["Score"] = 9 }
        };

        Assert.Equal(9, exp.Execute(RowsContext(rows)));
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, true, true)]
    public void Or_ComposesWithAnd(bool first, bool second, bool third, bool expected)
    {
        var exp = ExpressionParser.Parse("or(and(First, Second), Third)");
        var context = new ObjectContext(new Dictionary<Lookup, object?>
        {
            ["First"] = first,
            ["Second"] = second,
            ["Third"] = third
        });

        Assert.Equal(expected, exp.Execute(context));
        Assert.Equal([new PropertyLookup("First"), new PropertyLookup("Second"), new PropertyLookup("Third")],
            exp.Properties);
    }

    [Fact]
    public void TestFunction()
    {
        var exp = ExpressionParser.Parse("addDays(now, 5)");
        var context = new ObjectContext(new Dictionary<Lookup, object?>());

        var res = exp.Execute(context);

        Assert.IsType<DateTime>(res);
        var date = (DateTime)res;
        Assert.Equal(DateTime.Now.AddDays(5).Date, date.Date);
    }

    [Fact]
    public void TestIdentifier()
    {
        var exp = ExpressionParser.Parse("Boop.Beep");
        var context = new ObjectContext(new Dictionary<Lookup, object?> { ["Boop.Beep"] = 3 });

        var res = exp.Execute(context);

        Assert.Equal(3, res);
    }

    [Fact]
    public void TestProperties()
    {
        var exp = ExpressionParser.Parse("addDays(Boop.Beep,Oink)");

        var res = exp.Properties;

        Assert.Equal(["Boop.Beep", "Oink"], res);
    }

    [Fact]
    public void TestCondition()
    {
        var exp = ExpressionParser.Parse("find(Boop,Beep == 3)");

        Assert.IsType<Call>(exp);

        var call = (Call)exp;

        Assert.Equal(2, call.Arguments.Length);
        Assert.IsType<Operator>(call.Arguments[1]);

        var op = (Operator)call.Arguments[1];

        Assert.Equal(OperatorType.Equal, op.Type);
        Assert.Equivalent(new Identifier("Beep"), op.Left);
        Assert.Equivalent(new Int(3), op.Right);

        Assert.Single(exp.Properties, p => p is ComplexLookup);
    }

    [Fact]
    public void Equality_ExecutesAndReportsReferencedProperties()
    {
        var exp = ExpressionParser.Parse("SupervisorAccess.LoginMethod == =Uva");
        var context = new ObjectContext(new Dictionary<Lookup, object?>
        {
            ["SupervisorAccess.LoginMethod"] = "Uva"
        });

        Assert.Equal(true, exp.Execute(context));
        Assert.Equal([new PropertyLookup("SupervisorAccess.LoginMethod")], exp.Properties);
    }

    [Fact]
    public void TestDate_DaysAfter()
    {
        var exp = ExpressionParser.Parse("addDays(now, 5)");
        var context = new ObjectContext(new Dictionary<Lookup, object?>());

        var res = exp.Execute(context);

        Assert.IsType<DateTime>(res);
        var date = (DateTime)res;
        Assert.Equal(DateTime.Now.AddDays(5).Date, date.Date);
    }

    [Fact]
    public void TestDate_WeeksAfter()
    {
        var exp = ExpressionParser.Parse("addWeeks(now, 5)");
        var context = new ObjectContext(new Dictionary<Lookup, object?>());

        var res = exp.Execute(context);

        Assert.IsType<DateTime>(res);
        var date = (DateTime)res;
        Assert.Equal(DateTime.Now.AddDays(5 * 7).Date, date.Date);
    }

    [Fact]
    public void TestDate_MonthsAfter()
    {
        var exp = ExpressionParser.Parse("addMonths(now, 5)");
        var context = new ObjectContext(new Dictionary<Lookup, object?>());

        var res = exp.Execute(context);

        Assert.IsType<DateTime>(res);
        var date = (DateTime)res;
        Assert.Equal(DateTime.Now.AddMonths(5).Date, date.Date);
    }

    [Fact]
    public void TestDate_CombinedAfter()
    {
        var exp = ExpressionParser.Parse("addDays(addMonths(now, 5), 3)");
        var context = new ObjectContext(new Dictionary<Lookup, object?>());

        var res = exp.Execute(context);

        Assert.IsType<DateTime>(res);
        var date = (DateTime)res;
        Assert.Equal(DateTime.Now.AddMonths(5).AddDays(3).Date, date.Date);
    }

    [Fact]
    public void TestDate_FormatIsoRoundTrip()
    {
        var date = new DateTime(2026, 3, 16, 12, 34, 56, DateTimeKind.Local);
        var exp = ExpressionParser.Parse("formatDate(TestDate, =o)");
        var context = new ObjectContext(new Dictionary<Lookup, object?> { ["TestDate"] = date });

        var res = exp.Execute(context);

        var output = Assert.IsType<string>(res);
        Assert.Equal(date.ToString("o"), output);
        Assert.True(DateTimeOffset.TryParse(output, out _));
    }

    [Fact]
    public void TestDate_FormatNullDate_ReturnsNull()
    {
        var exp = ExpressionParser.Parse("formatDate(MissingDate, =o)");
        var context = new ObjectContext(new Dictionary<Lookup, object?>());

        var res = exp.Execute(context);

        Assert.Null(res);
    }

    [Theory]
    [InlineData("2026-03-16T12:34:56+01:00", 1)]
    [InlineData("2026-03-16T11:34:56Z", 0)]
    [InlineData("2026-03-16T07:34:56-04:00", -4)]
    public void TestDeadline_EvaluateDatetimeStringWithTimezone(string value, int offsetHours)
    {
        var deadline = new Deadline { Date = "TestDate" };
        var context = new ObjectContext(new Dictionary<Lookup, object?> { ["TestDate"] = value });

        var res = deadline.Evaluate(context);

        Assert.NotNull(res);
        Assert.Equal(new DateTimeOffset(2026, 3, 16, 11, 34, 56, TimeSpan.Zero), res.Value.ToUniversalTime());
        Assert.Equal(TimeSpan.FromHours(offsetHours), res.Value.Offset);
    }

    [Fact]
    public void TestDeadline_EvaluateDateTimeOffsetWithTimezone()
    {
        var date = new DateTimeOffset(2026, 3, 16, 12, 34, 56, TimeSpan.FromHours(1));
        var deadline = new Deadline { Date = "TestDate" };
        var context = new ObjectContext(new Dictionary<Lookup, object?> { ["TestDate"] = date });

        var res = deadline.Evaluate(context);

        Assert.NotNull(res);
        Assert.Equal(date, res);
        Assert.Equal(TimeSpan.FromHours(1), res.Value.Offset);
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public void TestDeadline_EvaluateDateTimePreservesTimezone(DateTimeKind kind)
    {
        var date = new DateTime(2026, 3, 16, 12, 34, 56, kind);
        var deadline = new Deadline { Date = "TestDate" };
        var context = new ObjectContext(new Dictionary<Lookup, object?> { ["TestDate"] = date });

        var res = deadline.Evaluate(context);

        Assert.NotNull(res);
        Assert.Equal(date, res.Value.DateTime);
        Assert.Equal(kind == DateTimeKind.Utc ? TimeSpan.Zero : TimeZoneInfo.Local.GetUtcOffset(date),
            res.Value.Offset);
    }
}