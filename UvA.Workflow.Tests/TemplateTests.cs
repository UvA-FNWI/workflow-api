using UvA.Workflow.Expressions;
using UvA.Workflow.Tools;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class TemplateTests
{
    [Theory]
    [InlineData("dateLong", "07-09-2026")]
    [InlineData("dateShort", "07/09")]
    public void DateHelpers_FormatDatesAndTrackLookup(string helper, string expected)
    {
        var template = new Template("{{ " + helper + "(LastEvent) }}");
        var context = new ObjectContext(new Dictionary<Lookup, object?>
        {
            ["LastEvent"] = new DateTime(2026, 9, 7)
        });

        Assert.Equal(expected, template.Apply(context));
        Assert.Equal(["LastEvent"], template.Properties.AsSpan());
    }

    [Theory]
    [InlineData("dateLong")]
    [InlineData("dateShort")]
    public void DateHelpers_RenderMissingDatesAsEmpty(string helper)
    {
        var template = new Template("{{ " + helper + "(LastEvent) }}");
        Assert.Equal("", template.Apply(new ObjectContext([])));
    }

    [Theory]
    [InlineData("{{ dateLong() }}")]
    [InlineData("{{ dateShort(LastEvent, LastEvent) }}")]
    public void DateHelpers_RejectWrongNumberOfArguments(string source)
    {
        var exception = Assert.Throws<Exception>(() => new Template(source).Apply(new ObjectContext([])));
        Assert.Equal("Invalid number of arguments", exception.Message);
    }

    [Fact]
    public void TestProperties()
    {
        var template = new Template("""
                                    Dear {{ Submitter }},

                                    Your proposal {{ Request.Title }} has unfortunately been rejected. Comment: {{ Decision.Comment }}.

                                    Have a good 2024!
                                    """);
        Assert.Equal(["Submitter", "Request.Title", "Decision.Comment"], template.Properties.AsSpan());
    }

    [Fact]
    public void TestApply()
    {
        var template = new Template("{{a}} is a {{b}}, yes?");
        var objectContext = new ObjectContext(new Dictionary<Lookup, object?>
        {
            ["a"] = "rabbit",
            ["b"] = "donkey"
        });
        var result = template.Apply(objectContext);

        Assert.Equal("rabbit is a donkey, yes?", result);
    }
}