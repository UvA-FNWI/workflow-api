using MongoDB.Bson;
using Moq;
using UvA.Workflow.Assessments;
using UvA.Workflow.Events;
using UvA.Workflow.Tools;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.WorkflowInstances;

public class LinkedStepResolutionTests
{
    [Theory]
    [InlineData("Sequential")]
    [InlineData("Parallel")]
    public async Task Resolve_GroupWaitsForAllChildrenAndReopensAfterSourceUndo(string hierarchyMode)
    {
        var setup = new LinkedStepTestSetup(LinkedStepConfigurationTests.GroupedFiles(hierarchyMode));

        Assert.Equal("SharedUpload", (await setup.Resolver.Resolve(setup.Student, default)).CurrentStep);
        setup.Complete("Uploaded", 20);
        Assert.Equal("SharedUpload", (await setup.Resolver.Resolve(setup.Student, default)).CurrentStep);

        setup.Complete("Reviewed", 21);
        var context = await setup.Resolver.Resolve(setup.Student, default);
        Assert.Equal("SharedAssessment", context.CurrentStep);
        Assert.Equal(new DateTime(2026, 6, 21, 11, 0, 0, DateTimeKind.Utc),
            setup.Model.WorkflowDefinitions["Individual"].Steps[1]
                .GetEndDate(setup.Student, setup.Model.WorkflowDefinitions["Individual"], context));

        setup.Source.Events.Remove("Reviewed");
        Assert.Equal("SharedUpload", (await setup.Resolver.Resolve(setup.Student, default)).CurrentStep);
        Assert.Single(setup.Student.Events);
        Assert.Empty(setup.Student.Properties.Keys.Except(["SharedThesis"]));
    }

    [Fact]
    public async Task Resolve_GroupUsesNestedAndConditionalSourceChildren()
    {
        var files = LinkedStepConfigurationTests.GroupedFiles();
        files["Shared/Entity.yaml"] += "\n  - name: NeedsReview\n    type: Check";
        files["Shared/Steps/UploadDocument.yaml"] = "name: UploadDocument\nchildren: [Document]";
        files["Shared/Steps/Document.yaml"] = "name: Document\nends:\n  event: Uploaded";
        files["Shared/Steps/ReviewDocument.yaml"] +=
            "\ncondition:\n  value:\n    property: NeedsReview\n    equal: true";
        var setup = new LinkedStepTestSetup(files);
        setup.Source.Properties["NeedsReview"] = false;
        setup.Complete("Uploaded", 20);

        var context = await setup.Resolver.Resolve(setup.Student, default);
        Assert.Equal("SharedAssessment", context.CurrentStep);
        Assert.Equal(new DateTime(2026, 6, 20, 11, 0, 0, DateTimeKind.Utc),
            setup.Model.WorkflowDefinitions["Individual"].Steps[1]
                .GetEndDate(setup.Student, setup.Model.WorkflowDefinitions["Individual"], context));

        setup.Source.Properties["NeedsReview"] = true;
        Assert.Equal("SharedUpload", (await setup.Resolver.Resolve(setup.Student, default)).CurrentStep);
        setup.Complete("Reviewed", 21);
        Assert.Equal("SharedAssessment", (await setup.Resolver.Resolve(setup.Student, default)).CurrentStep);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resolve_GroupCompletionDoesNotWaitForOptionalChildren(bool optionalCompleted)
    {
        var files = LinkedStepConfigurationTests.GroupedFiles();
        files["Shared/Steps/ReviewDocument.yaml"] += "\nmode: Optional";
        var setup = new LinkedStepTestSetup(files);
        setup.Complete("Uploaded", 20);
        if (optionalCompleted) setup.Complete("Reviewed", 21);

        var context = await setup.Resolver.Resolve(setup.Student, default);

        Assert.Equal("Assess", setup.Model.FindOpenStep(setup.Source)?.Name);
        Assert.Equal("SharedAssessment", context.CurrentStep);
        Assert.Equal(new DateTime(2026, 6, 20, 11, 0, 0, DateTimeKind.Utc),
            setup.Model.WorkflowDefinitions["Individual"].Steps[1]
                .GetEndDate(setup.Student, setup.Model.WorkflowDefinitions["Individual"], context));
    }

    [Fact]
    public async Task Resolve_UsesSharedCompletionInsteadOfAStaleLocalStep()
    {
        var setup = new LinkedStepTestSetup();
        setup.Student.CurrentStep = "After";
        setup.Complete("Uploaded", 20);

        var context = await setup.Resolver.Resolve(setup.Student, default);

        Assert.Equal("SharedAssessment", setup.Model.GetCurrentStep(setup.Student, context)?.Name);
        Assert.DoesNotContain("After", setup.Model.GetActiveSteps(setup.Student, context));
        Assert.Equal("SharedAssessment", context.CurrentStep);
        Assert.Equal("SharedAssessment", context.Get("CurrentStep"));
        Assert.Equal("After", setup.Student.CurrentStep);
        Assert.Single(setup.Student.Events);
        Assert.False(setup.Student.Events.ContainsKey("Uploaded"));

        setup.Complete("Assessed", 21);
        context = await setup.Resolver.Resolve(setup.Student, default);
        Assert.Equal("After", context.CurrentStep);
        Assert.Equal("After", setup.Model.GetCurrentStep(setup.Student, context)?.Name);

        setup.Source.Events.Remove("Assessed");
        context = await setup.Resolver.Resolve(setup.Student, default);
        Assert.Equal("SharedAssessment", context.CurrentStep);
        Assert.Equal("SharedAssessment", setup.Model.GetCurrentStep(setup.Student, context)?.Name);
    }

    [Fact]
    public async Task Resolve_KeepsTheInternalStepNameWhenDisplayEnrichmentUsesItsTitle()
    {
        var setup = new LinkedStepTestSetup();
        setup.Complete("Uploaded", 20);
        var context = await setup.Resolver.Resolve(setup.Student, default);

        await setup.Enricher.Enrich([
            new EnrichmentBatch(setup.Model.WorkflowDefinitions["Individual"], [context], [])
        ], default);

        Assert.Equal("SharedAssessment", context.CurrentStep);
        Assert.Equal("Joint assessment", Assert.IsType<BilingualString>(context.Get("CurrentStep")).En);
    }

    [Theory]
    [InlineData("unset")]
    [InlineData("missing")]
    [InlineData("wrong-type")]
    [InlineData("unknown-type")]
    [InlineData("invalid-id")]
    public async Task Resolve_UnavailableSourceDoesNotSkipRequiredWork(string failure)
    {
        var setup = new LinkedStepTestSetup();
        if (failure == "unset") setup.Student.Properties.Remove("SharedThesis");
        if (failure == "missing") setup.Sources.Clear();
        if (failure == "wrong-type") setup.Source.WorkflowDefinition = "Individual";
        if (failure == "unknown-type") setup.Source.WorkflowDefinition = "DoesNotExist";
        if (failure == "invalid-id") setup.Student.Properties["SharedThesis"] = "not-an-id";

        var context = await setup.Resolver.Resolve(setup.Student, default);
        var step = setup.Model.WorkflowDefinitions["Individual"].Steps[1];

        Assert.Equal("SharedUpload", setup.Model.GetCurrentStep(setup.Student, context)?.Name);
        Assert.True(step.IsVisible(context));
        Assert.False(step.HasEnded(context));
        Assert.Null(context.ImportedSteps["SharedUpload"]);
    }

    [Fact]
    public async Task Resolve_BatchesFourStudentsAndDoesNotChangeTheirHistories()
    {
        var setup = new LinkedStepTestSetup();
        setup.Complete("Uploaded", 20);
        setup.Complete("Assessed", 21);
        var students = Enumerable.Range(0, 4).Select(_ => setup.CreateStudent()).ToArray();

        var contexts = await setup.Resolver.Resolve(students, default);

        Assert.All(students, student =>
        {
            Assert.Equal("After", setup.Model.GetCurrentStep(student, contexts[student.Id])?.Name);
            Assert.Single(student.Events);
        });
        setup.Repository.Verify(repo => repo.GetByIds(
            It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { setup.Source.Id })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resolve_EnrichesTheSourceConditionAndKeepsItsContextSeparate()
    {
        var files = LinkedStepConfigurationTests.Files();
        files["Course/Entity.yaml"] = "name: Course\nproperties:\n  - name: Enabled\n    type: Check";
        files["Shared/Entity.yaml"] += "\n  - name: Course\n    type: Course!";
        files["Shared/Steps/Upload.yaml"] += "\ncondition:\n  value:\n    property: Course.Enabled\n    equal: true";
        var setup = new LinkedStepTestSetup(files);
        var courseId = ObjectId.GenerateNewId();
        setup.Source.Properties["Course"] = courseId.ToString();
        setup.Repository.Setup(repo => repo.GetAllById(It.IsAny<string[]>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new() { ["_id"] = courseId, ["Enabled"] = false }]);

        var context = await setup.Resolver.Resolve(setup.Student, default);

        Assert.False(setup.Model.WorkflowDefinitions["Individual"].Steps[1].IsVisible(context));
        Assert.Equal("SharedAssessment", setup.Model.GetCurrentStep(setup.Student, context)?.Name);
        Assert.Equal(setup.Source.Id, setup.Student.GetProperty("SharedThesis")!.AsString);
    }

    [Fact]
    public async Task CompletionDate_UsesTheFinalSharedStepWithoutChangingLastEvent()
    {
        var files = LinkedStepConfigurationTests.Files();
        files["Individual/Entity.yaml"] = files["Individual/Entity.yaml"].Replace(
            "[Before, SharedUpload, SharedAssessment, After]", "[Before, SharedUpload, SharedAssessment]");
        var setup = new LinkedStepTestSetup(files);
        setup.Complete("Uploaded", 20);
        setup.Complete("Assessed", 21);

        var context = await setup.Resolver.Resolve(setup.Student, default);

        Assert.Null(setup.Model.GetCurrentStep(setup.Student, context));
        Assert.Null(context.CurrentStep);
        Assert.Equal(new DateTime(2026, 6, 21, 11, 0, 0, DateTimeKind.Utc),
            setup.Model.GetCompletionDate(setup.Student, context));
        Assert.Equal(setup.Student.Events["Started"].Date!.Value.ToLocalTime(), context.Get("LastEvent"));
    }
}

internal sealed class LinkedStepTestSetup
{
    public ModelService Model { get; }
    public Mock<IWorkflowInstanceRepository> Repository { get; } = new();
    public WorkflowContextEnricher Enricher { get; }
    public WorkflowStateResolver Resolver { get; }
    public List<WorkflowInstance> Sources { get; } = [];
    public WorkflowInstance Source { get; }
    public WorkflowInstance Student { get; }

    public LinkedStepTestSetup(Dictionary<string, string>? files = null)
    {
        Model = new ModelService(
            new ModelParser(new DictionaryProvider(files ?? LinkedStepConfigurationTests.Files())));
        Enricher = new WorkflowContextEnricher(Repository.Object, Model, Mock.Of<IAssessmentService>());
        Resolver = new WorkflowStateResolver(Repository.Object, Model, Enricher);
        Source = new WorkflowInstance
        {
            Id = ObjectId.GenerateNewId().ToString(),
            WorkflowDefinition = "Shared",
            CreatedOn = new DateTime(2026, 1, 1),
            Properties = [],
            Events = []
        };
        Sources.Add(Source);
        Repository.Setup(repo => repo.GetByIds(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) =>
                Sources.Where(source => ids.Contains(source.Id)).ToArray());
        Student = CreateStudent();
    }

    public WorkflowInstance CreateStudent() => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        WorkflowDefinition = "Individual",
        CurrentStep = "Before",
        CreatedOn = new DateTime(2026, 1, 1),
        Properties = new() { ["SharedThesis"] = Source.Id },
        Events = new() { ["Started"] = new InstanceEvent { Id = "Started", Date = new DateTime(2026, 6, 1) } }
    };

    public void Complete(string name, int day)
        => Source.Events[name] = new InstanceEvent
            { Id = name, Date = new DateTime(2026, 6, day, 11, 0, 0, DateTimeKind.Utc) };
}