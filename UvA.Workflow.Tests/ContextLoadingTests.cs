using MongoDB.Bson;
using Moq;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Tools;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests;

public class ContextLoadingTests
{
    [Fact]
    public async Task CreateContext_LoadsOnlyRequestedInputsAndPreservesReferenceIds()
    {
        var (model, repository, service) = Create();
        var instance = new WorkflowInstance
        {
            WorkflowDefinition = "Project", Properties = new() { ["Target"] = "target", ["Unused"] = "missing" },
            Events = []
        };

        var context = await service.CreateContext(instance, ["DisplayName"], default);

        Assert.Equal("Organisation name", context.Get("DisplayName"));
        Assert.Equal("target", context.Get("Target"));
        Assert.Equal("Organisation name", context.Get("Target.Name"));
        Assert.Equal("missing", context.Get("Unused"));
        repository.Verify(r => r.GetAllById(new[] { "target" },
                It.Is<Dictionary<string, string>>(p => p.Count == 1 && p["Name"] == "$Properties.Name"), default),
            Times.Once);
    }

    [Fact]
    public async Task Enrich_LoadsSharedReferencesOnceForProjectedRows()
    {
        var (model, repository, service) = Create();
        Dictionary<string, BsonValue>[] rows =
        [
            new() { ["_id"] = "first", ["Target"] = "target" },
            new() { ["_id"] = "second", ["Target"] = "target" }
        ];

        var contexts = rows.Select(row => model.CreateContext("Project", row, ["Target"])).ToList();
        await service.Enrich(model.WorkflowDefinitions["Project"], contexts, ["DisplayName"], default,
            replaceStep: false);

        Assert.Equal(["first", "second"], contexts.Select(context => context.Id));
        Assert.All(contexts, context =>
        {
            Assert.Equal("Organisation name", context.Get("DisplayName"));
            Assert.Equal("target", context.Get("Target"));
        });
        repository.Verify(r => r.GetAllById(It.IsAny<string[]>(),
            It.IsAny<Dictionary<string, string>>(), default), Times.Once);
    }

    [Fact]
    public async Task CreateContext_KeepsMissingDisplayValuesBlankButRejectsConditionsUsingThem()
    {
        var (_, repository, service) = Create();
        repository.Setup(r => r.GetAllById(It.IsAny<string[]>(),
            It.IsAny<Dictionary<string, string>>(), default)).ReturnsAsync([]);
        var instance = new WorkflowInstance
        {
            WorkflowDefinition = "Project", Properties = new() { ["Target"] = "missing" }, Events = []
        };

        var context = await service.CreateContext(instance, ["DisplayName"], default);
        Assert.Null(context.Get("DisplayName"));
        var condition = new Condition
        {
            Not = true, Value = new Value { Property = "DisplayName", Equal = "=Blocked" }
        };
        Assert.Throws<InvalidWorkflowStateException>(() => condition.IsMet(context));
        var copied = context.WithValues(new Dictionary<Lookup, object?> { ["Response"] = "OK" });
        Assert.Throws<InvalidWorkflowStateException>(() => condition.IsMet(copied));
    }

    [Fact]
    public async Task ContextOverlay_PreservesLoadedFieldsUnlessTheirReferenceIsOverridden()
    {
        var (_, _, service) = Create();
        var instance = new WorkflowInstance
        {
            WorkflowDefinition = "Project", Properties = new() { ["Target"] = "target" }, Events = []
        };
        var context = await service.CreateContext(instance, ["DisplayName"], default);

        var response = context.WithValues(new Dictionary<Lookup, object?> { ["Response"] = "OK" });
        var overridden = response.WithValues(new Dictionary<Lookup, object?> { ["Target"] = "other" });

        Assert.Equal("Organisation name", response.Get("Target.Name"));
        Assert.Equal("OK", response.Get("Response"));
        Assert.Equal("other", overridden.Get("Target"));
        Assert.Null(overridden.Get("Target.Name"));
        Assert.Equal("target", context.Get("Target"));
    }

    [Fact]
    public async Task TargetForm_DoesNotTreatMissingReferencedInputsAsFalse()
    {
        var model = DerivedPropertyTests.Parse("name: Project\nproperties:\n  - name: Target\n    type: Target", new()
        {
            ["Target/Entity.yaml"] = """
                                     name: Target
                                     properties:
                                       - name: Blocked
                                         type: Check
                                       - name: IsBlocked
                                         type: Check
                                         expression: Blocked
                                       - name: Detail
                                         type: String!
                                         condition:
                                           not: true
                                           value:
                                             property: IsBlocked
                                             equal: true
                                     """,
            ["Target/Forms/Edit.yaml"] = "name: Edit\npages:\n  - name: Main\n    elements:\n      - question: Detail",
            ["Project/Forms/EditTarget.yaml"] = "name: EditTarget\nproperty: Target\ntargetForm: Edit"
        });
        var repository = new Mock<IWorkflowInstanceRepository>();
        repository.Setup(r => r.GetAllById(It.IsAny<string[]>(),
            It.IsAny<Dictionary<string, string>>(), default)).ReturnsAsync([]);
        var service = new InstanceService(repository.Object, model, null!, null!, null!, null!);
        var instance = new WorkflowInstance
        {
            WorkflowDefinition = "Project", Properties = new() { ["Target"] = "missing" }, Events = []
        };

        await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
            service.GetQuestionStatus(instance, model.GetForm(instance, "EditTarget"), true, default));
    }

    private static (ModelService, Mock<IWorkflowInstanceRepository>, InstanceService) Create()
    {
        var model = DerivedPropertyTests.Parse("""
                                               name: Project
                                               properties:
                                                 - name: Target
                                                   type: Target
                                                 - name: Unused
                                                   type: Target
                                                 - name: DisplayName
                                                   type: String
                                                   expression: Target.Name
                                               """, new()
        {
            ["Target/Entity.yaml"] = "name: Target\nproperties:\n  - name: Name\n    type: String"
        });
        var repository = new Mock<IWorkflowInstanceRepository>();
        repository.Setup(r => r.GetAllById(It.IsAny<string[]>(),
                It.IsAny<Dictionary<string, string>>(), default))
            .ReturnsAsync([new() { ["_id"] = "target", ["Name"] = "Organisation name" }]);
        var service = new InstanceService(repository.Object, model, null!, null!, null!, null!);
        return (model, repository, service);
    }
}