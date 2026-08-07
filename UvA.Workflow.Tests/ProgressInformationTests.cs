using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests;

public class ProgressInformationTests
{
    private readonly ModelService _modelService = new(UnitTestsHelpers.CreateModelParser());

    [Fact]
    public void Resolve_UsesFallbackProgressWhenNoConditionMatches()
    {
        var context = CreateContext();

        var progress = Resolve(context);

        Assert.Equal("Writing proposal", progress.Text.En);
        Assert.Equal("Schrijft voorstel", progress.Text.Nl);
        Assert.Equal(StatusColor.Green, progress.Color);
    }

    [Fact]
    public void Resolve_UsesMatchingConditionalProgressEntry()
    {
        var context = CreateContext(("RejectSubject", DateTime.UtcNow));

        var progress = Resolve(context);

        Assert.Equal("Revising proposal", progress.Text.En);
        Assert.Equal("Past voorstel aan", progress.Text.Nl);
        Assert.Equal(StatusColor.Red, progress.Color);
    }

    [Fact]
    public void Resolve_UsesFirstMatchingEntry()
    {
        var step = _modelService.WorkflowDefinitions["Project"].AllSteps
            .Single(candidate => candidate.Name == "Start");
        step.Progress.Insert(0, new ProgressInformation
        {
            Condition = new Condition { Event = new EventCondition { Id = "RejectSubject" } },
            Color = StatusColor.Green,
            Text = new BilingualString("First match", "Eerste overeenkomst")
        });
        var context = CreateContext(("RejectSubject", DateTime.UtcNow));

        var progress = Resolve(context);

        Assert.Equal("First match", progress.Text.En);
        Assert.Equal(StatusColor.Green, progress.Color);
    }

    [Fact]
    public void EffectiveCondition_PrefersConditionOverEventShorthand()
    {
        var condition = new Condition { Event = new EventCondition { Id = "ExplicitEvent" } };
        var progress = new ProgressInformation
        {
            Event = "ShorthandEvent",
            Condition = condition
        };

        Assert.Same(condition, progress.EffectiveCondition);
    }

    [Fact]
    public void Resolve_DoesNotUseEntryForSuppressedEvent()
    {
        var rejectedAt = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var resubmittedAt = rejectedAt.AddDays(1);
        var context = CreateContext(("RejectSubject", rejectedAt), ("Start", resubmittedAt));

        var progress = Resolve(context);

        Assert.Equal("Writing proposal", progress.Text.En);
        Assert.Equal(StatusColor.Green, progress.Color);
    }

    [Fact]
    public void Resolve_UsesStepTitleWhenNothingMatchesAndThereIsNoFallback()
    {
        var step = _modelService.WorkflowDefinitions["Project"].AllSteps
            .Single(candidate => candidate.Name == "Start");
        step.Progress =
        [
            new ProgressInformation
            {
                Condition = new Condition { Event = new EventCondition { Id = "RejectSubject" } },
                Color = StatusColor.Red,
                Text = new BilingualString("Revising proposal", "Past voorstel aan")
            }
        ];

        var progress = Resolve(CreateContext());

        Assert.Equal("Subject proposal", progress.Text.En);
        Assert.Equal("Indienen voorstel", progress.Text.Nl);
        Assert.Null(progress.Color);
    }

    [Fact]
    public void ModelParser_RejectsFallbackBeforeConditionalEntry()
    {
        var exception = Assert.Throws<Exception>(() => ParseProgressStep("""
                                                                         name: Start
                                                                         progress:
                                                                           - color: green
                                                                           - event: Submitted
                                                                             color: red
                                                                         """));

        Assert.Contains("Fallback progress entry in step Start must be last", exception.Message);
    }

    [Fact]
    public void ModelParser_RejectsMultipleFallbackEntries()
    {
        var exception = Assert.Throws<Exception>(() => ParseProgressStep("""
                                                                         name: Start
                                                                         progress:
                                                                           - color: green
                                                                           - color: red
                                                                         """));

        Assert.Contains("Step Start has more than one fallback progress entry", exception.Message);
    }

    private ObjectContext CreateContext(params (string EventId, DateTime Date)[] events)
    {
        var builder = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Start");
        foreach (var (eventId, date) in events)
            builder.WithEvent(eventId, date);
        return _modelService.CreateContext(builder.Build());
    }

    private ProgressInformationDto Resolve(ObjectContext context)
        => ProgressInformationDto.Resolve(_modelService.WorkflowDefinitions["Project"], "Start", context);

    private static ModelParser ParseProgressStep(string stepYaml)
        => new(new DictionaryProvider(new Dictionary<string, string>
        {
            ["TestWorkflow/Entity.yaml"] = """
                                           name: TestWorkflow
                                           titlePlural: Test workflows
                                           steps: [Start]
                                           """,
            ["TestWorkflow/Steps/Start.yaml"] = stepYaml
        }));
}