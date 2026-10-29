using MongoDB.Bson;
using Moq;
using UvA.Workflow.Assessments;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Notifications;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Tools;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.WorkflowInstances;

public class InstanceServiceEnrichTests
{
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly ModelService _modelService;
    private readonly Mock<IWorkflowInstanceRepository> _repository = new();
    private readonly InstanceService _service;

    public InstanceServiceEnrichTests()
    {
        _modelService = CreateModelService();
        _service = CreateInstanceService(_modelService, _repository);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enrich_PartialReferenceDoesNotEvaluateUnloadedComputedSource(bool array)
    {
        var model = DerivedPropertyTests.Parse("name: Project\nproperties:\n  - name: Target\n    type: '" +
            (array ? "[Target]" : "Target") + "'\n  - name: Display\n    type: " +
            (array ? "'[String]'" : "String!") + "\n    expression: Target.Name", new()
        {
            ["Target/Entity.yaml"] = "name: Target\nproperties:\n  - name: Name\n    type: String\n  - name: Code\n    type: String"
        });
        var id = ObjectId.GenerateNewId().ToString();
        var context = model.CreateContext("Project", new() { ["Target"] = array ? new BsonArray { id } : id });
        _repository.Setup(repository => repository.GetAllById(new[] { id },
                It.Is<Dictionary<string, string>>(projection => projection.Count == 1 && projection.ContainsKey("Code")), _ct))
            .ReturnsAsync([new() { ["_id"] = id, ["Code"] = "A1" }]);

        await CreateInstanceService(model, _repository).Enrich(model.WorkflowDefinitions["Project"], [context],
            ["Target.Code"], _ct, false);

        Assert.False(context.Values.ContainsKey("Display"));
        Assert.Equal("A1", array ? Assert.IsType<object[]>(context.Get("Target.Code"))[0] : context.Get("Target.Code"));
    }

    [Theory]
    [InlineData("select(Targets, =Name)")]
    [InlineData("select(except(Targets, Excluded), =Name)")]
    public async Task Enrich_SelectLoadsLiteralReferenceFieldBeforeComputing(string expression)
    {
        var model = DerivedPropertyTests.Parse("name: Project\nproperties:\n  - name: Targets\n    type: '[Target]'\n  - name: Excluded\n    type: '[Target]'\n  - name: Names\n    type: '[String]'\n    expression: " + expression, new()
        {
            ["Target/Entity.yaml"] = "name: Target\nproperties:\n  - name: Name\n    type: String"
        });
        var id = ObjectId.GenerateNewId().ToString();
        var context = model.CreateContext("Project", new()
        {
            ["Targets"] = new BsonArray { id }, ["Excluded"] = new BsonArray()
        });
        _repository.Setup(repository => repository.GetAllById(new[] { id },
                It.Is<Dictionary<string, string>>(projection => projection.ContainsKey("Name")), _ct))
            .ReturnsAsync([new() { ["_id"] = id, ["Name"] = "Target name" }]);
        Assert.False(context.Values.ContainsKey("Names"));

        await CreateInstanceService(model, _repository).Enrich(model.WorkflowDefinitions["Project"], [context],
            ["Names"], _ct, false);

        Assert.Equal(["Target name"], Assert.IsType<string[]>(context.Get("Names")));
    }

    [Fact]
    public void Context_SelectFromEmptyReferenceListReturnsEmptyArray()
    {
        var model = DerivedPropertyTests.Parse("name: Project\nproperties:\n  - name: Targets\n    type: '[Target]'\n  - name: Names\n    type: '[String]'\n    expression: select(Targets, =Name)", new()
        {
            ["Target/Entity.yaml"] = "name: Target\nproperties:\n  - name: Name\n    type: String"
        });

        var context = model.CreateContext("Project", new() { ["Targets"] = new BsonArray() });

        Assert.Empty(Assert.IsType<string[]>(context.Get("Names")));
    }

    [Fact]
    public async Task Enrich_ProjectedNullStillValidatesRequiredComputedProperty()
    {
        var model = DerivedPropertyTests.Parse("name: Project\nproperties:\n  - name: Target\n    type: Target\n  - name: Display\n    type: String!\n    expression: Target.Name", new()
        {
            ["Target/Entity.yaml"] = "name: Target\nproperties:\n  - name: Name\n    type: String"
        });
        var id = ObjectId.GenerateNewId().ToString();
        var context = model.CreateContext("Project", new() { ["Target"] = id });
        _repository.Setup(repository => repository.GetAllById(new[] { id },
                It.Is<Dictionary<string, string>>(projection => projection.ContainsKey("Name")), _ct))
            .ReturnsAsync([new() { ["_id"] = id }]);

        var error = await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
            CreateInstanceService(model, _repository).Enrich(model.WorkflowDefinitions["Project"], [context],
                ["Display"], _ct, false));

        Assert.Contains("A value is required", error.Message);
    }

    [Theory]
    [InlineData("count(Target.Values)", false, 2)]
    [InlineData("Target.Total", false, 2)]
    [InlineData("count(Target.Values)", true, 0)]
    [InlineData("Target.Total", true, 0)]
    public async Task Enrich_LoadsComputedSourcesThroughReferences(string expression, bool missing, int expected)
    {
        var model = new ModelService(new ModelParser(new DictionaryProvider(new Dictionary<string, string>
        {
            ["Source/Entity.yaml"] =
                "name: Source\nproperties:\n  - name: Target\n    type: Target\n  - name: Total\n    type: Int\n    expression: " +
                expression,
            ["Target/Entity.yaml"] =
                "name: Target\nproperties:\n  - name: Values\n    type: '[String]'\n  - name: Total\n    type: Int\n    expression: count(Values)"
        })));
        var targetId = ObjectId.GenerateNewId().ToString();
        var context = model.CreateContext("Source", new Dictionary<string, BsonValue> { ["Target"] = targetId });
        var service = CreateInstanceService(model, _repository);
        _repository.Setup(repository => repository.GetAllById(new[] { targetId },
                It.Is<Dictionary<string, string>>(projection => projection.Count == 1 &&
                                                                projection["Values"] == "$Properties.Values"), _ct))
            .ReturnsAsync([missing ? new() { ["_id"] = targetId }
                : new() { ["_id"] = targetId, ["Values"] = new BsonArray { "A", "B" } }]);

        await service.Enrich(model.WorkflowDefinitions["Source"], [context], ["Total"], _ct, false);

        Assert.Equal(expected, context.Get("Total"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enrich_ComputedReferenceAliasKeepsDottedFields(bool array)
    {
        var type = array ? "'[Target]'" : "Target";
        var model = DerivedPropertyTests.Parse($"name: Project\nproperties:\n  - name: Source\n    type: {type}\n  - name: Alias\n    type: {type}\n    expression: Source", new()
        {
            ["Target/Entity.yaml"] = "name: Target\nproperties:\n  - name: Name\n    type: String"
        });
        var id = ObjectId.GenerateNewId().ToString();
        var context = model.CreateContext("Project", new() { ["Source"] = array ? new BsonArray { id } : id });
        var service = CreateInstanceService(model, _repository);
        _repository.Setup(repository => repository.GetAllById(new[] { id },
                It.Is<Dictionary<string, string>>(projection => projection.ContainsKey("Name")), _ct))
            .ReturnsAsync([new() { ["_id"] = id, ["Name"] = "Target name" }]);

        await service.Enrich(model.WorkflowDefinitions["Project"], [context], ["Alias.Name"], _ct, false);

        if (array)
            Assert.Equal("Target name", Assert.IsType<object[]>(context.Get("Alias.Name"))[0]);
        else
            Assert.Equal("Target name", context.Get("Alias.Name"));
    }

    [Theory]
    [InlineData("if(ApprovedEventActive, =done, =pending)", "done")]
    [InlineData("dateLong(ApprovedEvent)", "15-10-2026")]
    [InlineData("dateLong(CreateDate)", "14-10-2026")]
    [InlineData("dateLong(LastEvent)", "15-10-2026")]
    [InlineData("CurrentStep", "Review")]
    public async Task Enrich_ProjectsBuiltInSourcesOfReferencedComputedProperty(string expression, string expected)
    {
        var model = DerivedPropertyTests.Parse("name: Project\nproperties:\n  - name: Target\n    type: Target", new()
        {
            ["Target/Entity.yaml"] = "name: Target\nevents:\n  - name: Approved\nproperties:\n  - name: Display\n    type: String\n    expression: " + expression
        });
        var id = ObjectId.GenerateNewId().ToString();
        var context = model.CreateContext("Project", new() { ["Target"] = id });
        var target = new BsonDocument
        {
            ["_id"] = id,
            ["CreatedOn"] = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc),
            ["CurrentStep"] = "Review",
            ["Events"] = new BsonDocument("Approved", new BsonDocument("Date",
                new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc)))
        };
        _repository.Setup(repository => repository.GetAllById(new[] { id },
                It.IsAny<Dictionary<string, string>>(), _ct))
            .ReturnsAsync((string[] _, Dictionary<string, string> projection, CancellationToken _) =>
            {
                var row = projection.ToDictionary(entry => entry.Key,
                    entry => BsonConversionTools.NavigateNestedBsonValue(target, entry.Value[1..].Split('.'))
                             ?? BsonNull.Value);
                row["_id"] = id;
                return [row];
            });

        await CreateInstanceService(model, _repository).Enrich(model.WorkflowDefinitions["Project"], [context],
            ["Target.Display"], _ct, false);

        Assert.Equal(expected, context.Get("Target.Display"));
    }

    [Fact]
    public async Task Enrich_SharedTargetAcrossBatches_CombinesIdsAndProjectedProperties()
    {
        var firstTargetId = ObjectId.GenerateNewId();
        var secondTargetId = ObjectId.GenerateNewId();
        var firstContext = Context("Shared", firstTargetId);
        var secondContext = Context("Shared", secondTargetId);

        _repository.Setup(repository => repository.GetAllById(
                It.Is<string[]>(ids =>
                    ids.Length == 2 &&
                    ids.Contains(firstTargetId.ToString()) &&
                    ids.Contains(secondTargetId.ToString())),
                It.Is<Dictionary<string, string>>(projection =>
                    projection.Count == 2 &&
                    projection["Name"] == "$Properties.Name" &&
                    projection["Type"] == "$Properties.Type"),
                _ct))
            .ReturnsAsync([
                Result(firstTargetId, ("Name", "First target"), ("Type", "First type")),
                Result(secondTargetId, ("Name", "Second target"), ("Type", "Second type"))
            ]);

        await _service.Enrich([
            new EnrichmentBatch(
                _modelService.WorkflowDefinitions["SourceA"],
                [firstContext],
                [new PropertyLookup("Shared.Name")]),
            new EnrichmentBatch(
                _modelService.WorkflowDefinitions["SourceB"],
                [secondContext],
                [new PropertyLookup("Shared.Type")])
        ], _ct, replaceStep: false);

        Assert.Equal("First target", firstContext.Get("Shared.Name"));
        Assert.Equal("First type", firstContext.Get("Shared.Type"));
        Assert.Equal("Second target", secondContext.Get("Shared.Name"));
        Assert.Equal("Second type", secondContext.Get("Shared.Type"));
        _repository.Verify(repository => repository.GetAllById(
            It.IsAny<string[]>(),
            It.IsAny<Dictionary<string, string>>(),
            _ct), Times.Once);
    }

    [Fact]
    public async Task Enrich_DifferentTargets_UsesSingleQueryAndTargetDefinitions()
    {
        var alphaId = ObjectId.GenerateNewId();
        var betaId = ObjectId.GenerateNewId();
        var alphaContext = Context("Alpha", alphaId);
        var betaContext = Context("Beta", betaId);

        _repository.Setup(repository => repository.GetAllById(
                It.Is<string[]>(ids =>
                    ids.Length == 2 &&
                    ids.Contains(alphaId.ToString()) &&
                    ids.Contains(betaId.ToString())),
                It.Is<Dictionary<string, string>>(projection =>
                    projection.Count == 2 &&
                    projection["Name"] == "$Properties.Name" &&
                    projection["Code"] == "$Properties.Code"),
                _ct))
            .ReturnsAsync([
                Result(alphaId, ("Name", "Alpha target")),
                Result(betaId, ("Code", "BETA"))
            ]);

        await _service.Enrich([
            new EnrichmentBatch(
                _modelService.WorkflowDefinitions["SourceA"],
                [alphaContext],
                [new PropertyLookup("Alpha.Name")]),
            new EnrichmentBatch(
                _modelService.WorkflowDefinitions["SourceB"],
                [betaContext],
                [new PropertyLookup("Beta.Code")])
        ], _ct, replaceStep: false);

        Assert.Equal("Alpha target", alphaContext.Get("Alpha.Name"));
        Assert.Equal("BETA", betaContext.Get("Beta.Code"));
        _repository.Verify(repository => repository.GetAllById(
            It.IsAny<string[]>(),
            It.IsAny<Dictionary<string, string>>(),
            _ct), Times.Once);
    }

    private static ObjectContext Context(string property, ObjectId targetId)
        => new(new Dictionary<Lookup, object?>
        {
            [property] = targetId.ToString()
        });

    private static Dictionary<string, BsonValue> Result(
        ObjectId id,
        params (string Property, string Value)[] properties)
    {
        var result = properties.ToDictionary(
            property => property.Property,
            property => (BsonValue)property.Value);
        result["_id"] = id;
        return result;
    }

    private static ModelService CreateModelService()
        => new(new ModelParser(new DictionaryProvider(new Dictionary<string, string>
        {
            ["TargetA/Entity.yaml"] = """
                                      name: TargetA
                                      properties:
                                        - name: Name
                                          type: String!
                                        - name: Type
                                          type: String!
                                      """,
            ["TargetB/Entity.yaml"] = """
                                      name: TargetB
                                      properties:
                                        - name: Code
                                          type: String!
                                      """,
            ["SourceA/Entity.yaml"] = """
                                      name: SourceA
                                      properties:
                                        - name: Shared
                                          type: TargetA!
                                        - name: Alpha
                                          type: TargetA!
                                      """,
            ["SourceB/Entity.yaml"] = """
                                      name: SourceB
                                      properties:
                                        - name: Shared
                                          type: TargetA!
                                        - name: Beta
                                          type: TargetB!
                                      """
        })));

    private static InstanceService CreateInstanceService(
        ModelService modelService,
        Mock<IWorkflowInstanceRepository> repository)
    {
        var userService = Mock.Of<IUserService>();
        var rightsService = new RightsService(modelService, userService, repository.Object);
        var layoutResolver = new Mock<IMailLayoutResolver>();
        layoutResolver.Setup(resolver => resolver.Resolve(It.IsAny<string?>()))
            .Returns(Mock.Of<IMailLayout>());

        return new InstanceService(
            repository.Object,
            modelService,
            userService,
            rightsService,
            UnitTestsHelpers.CreateMailBuilder(layoutResolver.Object),
            Mock.Of<IAssessmentService>());
    }
}
