using MongoDB.Bson;
using UvA.Workflow.Expressions;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class LastEventTests
{
    private readonly ModelService _modelService = new(UnitTestsHelpers.CreateModelParser());

    [Fact]
    public void FullInstance_UsesLatestDateRegardlessOfInsertionOrder()
    {
        var latest = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project").WithCurrentStep("Start")
            .WithEvent("Start", latest)
            .WithEvent("RejectSubject", latest.AddDays(-1))
            .WithEvent("Undated")
            .Build();

        var context = _modelService.CreateContext(instance);

        Assert.Equal(latest, context.Get("LastEvent"));
        Assert.Equal(latest.ToString(), new Template("{{ LastEvent }}").Apply(context));
        Assert.Equal("07-09-2026", new Template("{{ dateLong(LastEvent) }}").Apply(context));

        instance.Events.Remove("Start");
        Assert.Equal(latest.AddDays(-1), _modelService.CreateContext(instance).Get("LastEvent"));
    }

    [Fact]
    public void ProjectedInstance_NormalizesLastEvent()
    {
        var latest = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
        var definition = _modelService.WorkflowDefinitions["Project"];
        var context = ObjectContext.Create(definition, new Dictionary<string, BsonValue>
        {
            ["Events"] = new BsonDocument
            {
                ["Start"] = new BsonDocument("Date", latest),
                ["RejectSubject"] = new BsonDocument("Date", latest.AddDays(-1)),
                ["Undated"] = new BsonDocument("Date", BsonNull.Value)
            }
        });

        Assert.Equal(latest.ToLocalTime(), context.Get("LastEvent"));
        Assert.Equal("07/09", new Template("{{ dateShort(LastEvent) }}").Apply(context));
        Assert.Equal(DataType.DateTime, definition.GetDataType("LastEvent"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoDatedEvents_ReturnsNullAndRendersEmptyDate(bool projected)
    {
        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project").WithCurrentStep("Start").WithEvent("Undated").Build();
        var context = projected
            ? ObjectContext.Create(_modelService.WorkflowDefinitions["Project"], new Dictionary<string, BsonValue>
            {
                ["Events"] = new BsonDocument()
            })
            : _modelService.CreateContext(instance);

        Assert.Null(context.Get("LastEvent"));
        Assert.Equal("", new Template("{{ dateShort(LastEvent) }}").Apply(context));
    }

    [Theory]
    [InlineData("Entity.yaml", "name: Test\nsteps: [Start]\nevents: [{name: Last}]")]
    [InlineData("Steps/Start.yaml", "name: Start\nevents: [{name: Last}]")]
    [InlineData("Forms/Last.yaml", "name: Last")]
    [InlineData("Forms/Submit.yaml", "name: Submit\nonSubmit: [{event: Last}]")]
    [InlineData("Forms/Submit.yaml", "name: Submit\nonSave: [{event: Last}]")]
    [InlineData("Steps/Start.yaml", "name: Start\nactions: [{name: Finish, onAction: [{event: Last}]}]")]
    [InlineData("Entity.yaml",
        "name: Test\nsteps: [Start]\nglobalActions: [{name: Finish, onAction: [{event: Last}]}]")]
    public void Parser_RejectsReservedEventName(string path, string yaml)
    {
        var files = new Dictionary<string, string>
        {
            ["Test/Entity.yaml"] = "name: Test\nsteps: [Start]",
            ["Test/Steps/Start.yaml"] = "name: Start"
        };
        files["Test/" + path] = yaml;

        var exception = Assert.Throws<Exception>(() => new ModelParser(new DictionaryProvider(files)));

        Assert.Contains("Event name 'Last' is reserved", exception.Message);
    }
}