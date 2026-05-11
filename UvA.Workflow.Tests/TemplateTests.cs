using UvA.Workflow.Expressions;
using UvA.Workflow.Tools;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class TemplateTests
{
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