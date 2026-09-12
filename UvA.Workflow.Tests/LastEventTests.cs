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
        instance.CreatedOn = latest.AddDays(1);

        var context = _modelService.CreateContext(instance);

        Assert.Equal(latest, context.Get("LastEvent"));
        Assert.Equal(latest.ToString(), new Template("{{ LastEvent }}").Apply(context));
        Assert.Equal("07-09-2026", new Template("{{ dateLong(LastEvent) }}").Apply(context));

        instance.Events.Remove("Start");
        Assert.Equal(latest.AddDays(-1), _modelService.CreateContext(instance).Get("LastEvent"));

        instance.Events.Remove("RejectSubject");
        Assert.Equal(instance.CreatedOn, _modelService.CreateContext(instance).Get("LastEvent"));
    }

    [Fact]
    public void ProjectedInstance_NormalizesLastEventAndCreationDate()
    {
        var latest = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
        var definition = _modelService.WorkflowDefinitions["Project"];
        var context = ObjectContext.Create(definition, new Dictionary<string, BsonValue>
        {
            ["CreateDate"] = new BsonDateTime(latest.AddDays(-2)),
            ["Events"] = new BsonDocument
            {
                ["Start"] = new BsonDocument("Date", latest),
                ["RejectSubject"] = new BsonDocument("Date", latest.AddDays(-1)),
                ["Undated"] = new BsonDocument("Date", BsonNull.Value)
            }
        });

        Assert.Equal(latest.ToLocalTime(), context.Get("LastEvent"));
        Assert.Equal(latest.AddDays(-2).ToLocalTime(), context.Get("CreateDate"));
        Assert.Equal("07/09", new Template("{{ dateShort(LastEvent) }}").Apply(context));
        Assert.Equal(DataType.DateTime, definition.GetDataType("LastEvent"));
        Assert.Equal(DataType.DateTime, definition.GetDataType("CreateDate"));
        Assert.Equal("$CreatedOn", definition.GetKey("CreateDate"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NoDatedEvents_UsesCreationDate(bool projected, bool hasUndatedEvent)
    {
        var created = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project").WithCurrentStep("Start").Build();
        var events = new BsonDocument();
        if (hasUndatedEvent)
        {
            instance.Events["Undated"] = new() { Id = "Undated" };
            events["Undated"] = new BsonDocument("Date", BsonNull.Value);
        }

        instance.CreatedOn = created;
        var context = projected
            ? ObjectContext.Create(_modelService.WorkflowDefinitions["Project"], new Dictionary<string, BsonValue>
            {
                ["CreateDate"] = new BsonDateTime(created),
                ["Events"] = events
            })
            : _modelService.CreateContext(instance);

        Assert.Equal(projected ? created.ToLocalTime() : created, context.Get("LastEvent"));
        Assert.Equal("01/09", new Template("{{ dateShort(LastEvent) }}").Apply(context));
        Assert.Equal("01/09", new Template("{{ dateShort(CreateDate) }}").Apply(context));
        if (!projected)
            Assert.Equal("01/11", new Template("{{ dateShort(addMonths(CreateDate, 2)) }}").Apply(context));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProjectedInstance_WithoutEventOrCreationDates_RendersEmptyDate(bool hasEvents)
    {
        var rawData = new Dictionary<string, BsonValue>();
        if (hasEvents)
            rawData["Events"] = new BsonDocument();
        var context = ObjectContext.Create(_modelService.WorkflowDefinitions["Project"], rawData);

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