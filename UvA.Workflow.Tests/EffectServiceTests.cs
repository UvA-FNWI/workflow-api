using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using UvA.Workflow.Jobs;
using UvA.Workflow.Notifications;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests;

public class EffectServiceTests : ControllerTestsBase
{
    [Theory]
    [InlineData("Date", "=2027-01-08", 0)]
    [InlineData("DateTime", "=2027-01-08 17:00", 17)]
    [InlineData("DateTime", "addDays(=2027-01-07 17:00, 1)", 17)]
    public async Task SetProperty_StoresDatesAsBsonDates(string type, string expression, int hour)
    {
        PropertyDefinition property = type == "Date"
            ? new DatePropertyDefinition()
            : new DateTimePropertyDefinition();
        property.Name = "DeadlineProposalSubmit";
        property.Type = type;
        _modelService.WorkflowDefinitions["Project"].Properties.Add(property);
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();
        var effect = new Effect
        {
            SetProperty = new SetProperty { Property = property.Name, Value = expression }
        };

        await _effectService.RunEffect(new Job(), instance, effect, UnitTestsHelpers.AdminUser,
            _modelService.CreateContext(instance), _ct);

        var stored = Assert.IsType<BsonDateTime>(instance.GetProperty(property.Name));
        Assert.Equal(new DateTime(2027, 1, 8, hour, 0, 0), stored.ToLocalTime());
        Assert.Equal(new DateTime(2027, 1, 8, hour, 0, 0),
            _modelService.CreateContext(instance).Get(property.Name));
    }

    [Theory]
    [InlineData("EC", "42", 42)]
    [InlineData("Amount", "42.5", 42.5)]
    [InlineData("Title", "=2027-01-08 17:00", "2027-01-08 17:00")]
    [InlineData("Enabled", "true", true)]
    [InlineData("EndDate", "Missing", null)]
    public async Task SetProperty_PreservesScalarValues(string property, string expression, object? expected)
    {
        _modelService.WorkflowDefinitions["Project"].Properties.AddRange([
            new DoublePropertyDefinition { Name = "Amount", Type = "Double" },
            new CheckPropertyDefinition { Name = "Enabled", Type = "Check" }
        ]);
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();
        var effect = new Effect { SetProperty = new SetProperty { Property = property, Value = expression } };

        await _effectService.RunEffect(new Job(), instance, effect, UnitTestsHelpers.AdminUser,
            _modelService.CreateContext(instance), _ct);

        Assert.Equal(BsonValue.Create(expected), instance.GetProperty(property));
    }

    [Fact]
    public async Task SetProperty_RejectsInvalidDatesWithoutOverwritingTheProperty()
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();
        var original = new BsonDateTime(new DateTime(2027, 1, 8));
        instance.Properties["EndDate"] = original;
        var effect = new Effect { SetProperty = new SetProperty { Property = "EndDate", Value = "=invalid" } };

        await Assert.ThrowsAsync<FormatException>(() => _effectService.RunEffect(new Job(), instance, effect,
            UnitTestsHelpers.AdminUser, _modelService.CreateContext(instance), _ct));

        Assert.Equal(original, instance.GetProperty("EndDate"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetProperty_ObjectCopyPersistsOnlyStoredDescendants(bool array)
    {
        var type = array ? "[Party]" : "Party";
        var model = DerivedPropertyTests.Parse(
            $"name: Project\nproperties:\n  - name: Source\n    type: '{type}'\n  - name: Destination\n    type: '{type}'",
            new()
            {
                ["Party/Entity.yaml"] =
                    "name: Party\nisEmbedded: true\nproperties:\n  - name: Children\n    type: '[Child]'\n  - name: Total\n    type: Int\n    expression: count(Children)",
                ["Child/Entity.yaml"] =
                    "name: Child\nisEmbedded: true\nproperties:\n  - name: Name\n    type: String\n  - name: Display\n    type: String\n    expression: Name"
            });
        foreach (var definition in model.WorkflowDefinitions)
            _modelService.WorkflowDefinitions[definition.Key] = definition.Value;
        var stored = new BsonDocument("Children", new BsonArray { new BsonDocument("Name", "A") });
        BsonValue source = array ? new BsonArray { stored } : stored;
        var instance = new WorkflowInstance
        {
            Id = ObjectId.GenerateNewId().ToString(), WorkflowDefinition = "Project",
            Properties = new() { ["Source"] = source }, Events = []
        };
        UpdateDefinition<WorkflowInstance>? update = null;
        _workflowInstanceRepoMock.Setup(repository => repository.UpdateFields(instance.Id,
                It.IsAny<UpdateDefinition<WorkflowInstance>>(), _ct))
            .Callback<string, UpdateDefinition<WorkflowInstance>, CancellationToken>((_, value, _) => update = value)
            .Returns(Task.CompletedTask);
        var context = _modelService.CreateContext(instance);

        await _effectService.RunEffect(new Job(), instance,
            new Effect { SetProperty = new SetProperty { Property = "Destination", Value = "Source" } },
            UnitTestsHelpers.AdminUser, context, _ct);

        Assert.Equal(source, instance.Properties["Destination"]);
        Assert.NotNull(update);
        var saved = update.Render(new RenderArgs<WorkflowInstance>(BsonSerializer.LookupSerializer<WorkflowInstance>(),
            BsonSerializer.SerializerRegistry));
        Assert.Equal(source, saved["$set"]["Properties.Destination"]);
        Assert.Equal(1, array ? Assert.IsType<object[]>(context.Get("Source.Total"))[0] : context.Get("Source.Total"));
    }

    [Fact]
    public async Task AddEvent_KeepsTimestampIncreasingWhenRecordingAnExistingEvent()
    {
        var previous = DateTime.UtcNow.AddHours(1);
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();
        instance.RecordEvent("Start", previous);

        await _effectService.AddEvent(instance, "Start", UnitTestsHelpers.AdminUser, _ct);

        Assert.Equal(previous.AddMilliseconds(1), instance.Events["Start"].Date);
        Assert.Equal(DateTimeKind.Utc, instance.Events["Start"].Date!.Value.Kind);
    }

    /// <summary>
    /// Verifies that MailLogEntry.ExecutedBy records whatever user the caller passes into
    /// EffectService.RunEffect — i.e. the real admin, not the impersonated target.
    /// The controller-level tests (ActionsControllerTests, SubmissionsControllerTests) verify
    /// that the correct user enters the pipeline; this test verifies the pipeline writes it
    /// to the mail log rather than reading from somewhere else.
    /// </summary>
    [Fact]
    public async Task MailLog_ExecutedBy_RecordsUserPassedIn_NotImpersonatedTarget()
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();

        MailLogEntry? capturedEntry = null;
        _mailLogRepositoryMock
            .Setup(r => r.Log(It.IsAny<MailLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<MailLogEntry, CancellationToken>((entry, _) => capturedEntry = entry)
            .Returns(Task.CompletedTask);

        var job = new Job
        {
            Id = ObjectId.GenerateNewId().ToString(),
            InstanceId = instance.Id,
            Input = new JobInput(new MailMessage("Test", "Test"))
        };
        var effect = new Effect { SendMail = new SendMessage { SendAutomatically = true } };
        var context = _modelService.CreateContext(instance);

        // Pass AdminUser — exactly what the fixed call sites now supply.
        // Passing ImpersonatedTarget here instead would write the wrong Id and fail both assertions.
        await _effectService.RunEffect(job, instance, effect, UnitTestsHelpers.AdminUser, context, _ct);

        Assert.NotNull(capturedEntry);
        Assert.Equal(UnitTestsHelpers.AdminUser.Id, capturedEntry.ExecutedBy);
        Assert.NotEqual(UnitTestsHelpers.ImpersonatedTarget.Id, capturedEntry.ExecutedBy);
    }
}