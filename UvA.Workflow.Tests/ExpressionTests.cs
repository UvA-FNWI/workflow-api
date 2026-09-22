using UvA.Workflow.Expressions;
using UvA.Workflow.Tools;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests;

public class ExpressionTests
{
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
        Assert.Equivalent(new Number(3), op.Right);

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