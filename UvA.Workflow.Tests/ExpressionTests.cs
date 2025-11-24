using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Expressions;
using UvA.Workflow.Services;

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
}