using System.Text.Json;
using UvA.Workflow.Tools;
using UvA.Workflow.WorkflowInstances.ServiceCalls;

namespace UvA.Workflow.Tests.WorkflowInstances;

public class ServiceResponseSelectorTests
{
    private static readonly ObjectContext Context = new(new Dictionary<Lookup, object?>
    {
        ["CourseId"] = "42"
    });

    private static ServiceResponseSelection Selection() => new()
    {
        CollectionPath = "results",
        Match = new()
        {
            ["topic.id"] = "{{ CourseId }}",
            ["status"] = "open",
            ["visibility"] = "invitation"
        }
    };

    [Fact]
    public void Select_ReturnsOnlyTheMatchingObjectWithoutCountMetadata()
    {
        using var body = JsonDocument.Parse("""
                                            {"results":[
                                              {"id":1,"topic":{"id":99},"status":"open","visibility":"invitation"},
                                              {"id":2,"topic":{"id":42},"status":"closed","visibility":"invitation"},
                                              {"id":3,"topic":{"id":42},"status":"open","visibility":"public"},
                                              {"id":4,"topic":{"id":42},"status":"open","visibility":"invitation"}
                                            ]}
                                            """);

        var selected = ServiceResponseSelector.Select(body.RootElement, Selection(), Context);

        Assert.Equal(4, selected.GetProperty("id").GetInt32());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"topic\":{\"id\":99},\"status\":\"open\",\"visibility\":\"invitation\"}]")]
    [InlineData("[{\"status\":\"open\",\"visibility\":\"invitation\"}]")]
    [InlineData(
        "[{\"topic\":{\"id\":42},\"status\":\"open\",\"visibility\":\"invitation\"},{\"topic\":{\"id\":42},\"status\":\"open\",\"visibility\":\"invitation\"}]")]
    public void Select_RejectsZeroOrMultipleMatches(string rows)
    {
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            results = JsonSerializer.Deserialize<JsonElement>(rows)
        }));

        var error = Assert.Throws<InvalidOperationException>(() =>
            ServiceResponseSelector.Select(body.RootElement, Selection(), Context));

        Assert.Contains("exactly one", error.Message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"results\":{}}")]
    [InlineData("{\"results\":null}")]
    public void Select_RejectsUnexpectedResponseShapes(string json)
    {
        using var body = JsonDocument.Parse(json);

        Assert.Throws<InvalidOperationException>(() =>
            ServiceResponseSelector.Select(body.RootElement, Selection(), Context));
    }

    [Fact]
    public void Select_RejectsMissingCriteriaValues()
    {
        using var body = JsonDocument.Parse("""
                                            {"results":[
                                              {"topic":{"id":""},"status":"open","visibility":"invitation"}
                                            ]}
                                            """);

        Assert.Throws<InvalidOperationException>(() =>
            ServiceResponseSelector.Select(body.RootElement, Selection(), new ObjectContext(new())));
    }
}