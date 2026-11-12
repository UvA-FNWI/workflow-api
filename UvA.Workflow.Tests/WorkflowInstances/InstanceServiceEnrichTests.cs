using MongoDB.Bson;
using Moq;
using UvA.Workflow.Assessments;
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

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(2, false, true)]
    [InlineData(2, true, false)]
    [InlineData(1, true, true)]
    public async Task Enrich_NestedReferences_ResolvesRequestedDepth(
        int depth,
        bool alreadyLoaded,
        bool referenceArray)
    {
        var targetId = ObjectId.GenerateNewId();
        var targetIdString = targetId.ToString();

        object reference = referenceArray
            ? new[] { targetIdString }
            : targetIdString;

        if (alreadyLoaded)
        {
            var loadedValues = new Dictionary<Lookup, object?>
            {
                ["Id"] = targetIdString,
                ["Parent"] = targetIdString
            };

            reference = referenceArray
                ? new[] { loadedValues }
                : loadedValues;
        }

        var firstContext = new ObjectContext(new Dictionary<Lookup, object?>
        {
            ["Shared"] = reference
        });
        var secondContext = new ObjectContext(new Dictionary<Lookup, object?>
        {
            ["Shared"] = reference
        });

        _repository.Setup(repository => repository.GetAllById(
                It.Is<string[]>(ids =>
                    ids.Length == 1 && ids[0] == targetIdString),
                It.Is<Dictionary<string, string>>(projection =>
                    projection.Count == 1 &&
                    projection.ContainsKey("Parent") &&
                    projection["Parent"] == "$Properties.Parent"),
                _ct))
            .ReturnsAsync([Result(targetId, ("Parent", targetIdString))]);

        _repository.Setup(repository => repository.GetAllById(
                It.Is<string[]>(ids =>
                    ids.Length == 1 && ids[0] == targetIdString),
                It.Is<Dictionary<string, string>>(projection =>
                    projection.Count == 1 &&
                    projection.ContainsKey("Name") &&
                    projection["Name"] == "$Properties.Name"),
                _ct))
            .ReturnsAsync([Result(targetId, ("Name", "Advisor"))]);

        var lookup = "Shared."
                     + string.Concat(Enumerable.Repeat("Parent.", depth))
                     + "Name";

        EnrichmentBatch[] batches =
        [
            new(_modelService.WorkflowDefinitions["SourceA"],
                [firstContext], [new PropertyLookup(lookup)]),
            new(_modelService.WorkflowDefinitions["SourceB"],
                [secondContext], [new PropertyLookup(lookup)])
        ];

        await _service.Enrich(batches, _ct, replaceStep: false);

        foreach (var context in new[] { firstContext, secondContext })
        {
            if (referenceArray)
            {
                Assert.Equal(
                    new object?[] { "Advisor" },
                    Assert.IsType<object[]>(context.Get(lookup)));
            }
            else
            {
                Assert.Equal("Advisor", context.Get(lookup));
            }
        }

        var expectedQueries = alreadyLoaded ? depth : depth + 1;

        _repository.Verify(repository => repository.GetAllById(
            It.IsAny<string[]>(),
            It.IsAny<Dictionary<string, string>>(),
            _ct), Times.Exactly(expectedQueries));

        // Reusing the fully enriched contexts must not trigger more queries.
        await _service.Enrich(batches, _ct, replaceStep: false);

        _repository.Verify(repository => repository.GetAllById(
            It.IsAny<string[]>(),
            It.IsAny<Dictionary<string, string>>(),
            _ct), Times.Exactly(expectedQueries));

        foreach (var context in new[] { firstContext, secondContext })
        {
            if (referenceArray)
            {
                Assert.Equal(
                    new object?[] { "Advisor" },
                    Assert.IsType<object[]>(context.Get(lookup)));
            }
            else
            {
                Assert.Equal("Advisor", context.Get(lookup));
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Enrich_MissingReferencedInstance_ReturnsNull(
        bool missingFirstLevel)
    {
        var targetId = ObjectId.GenerateNewId();
        var parentId = ObjectId.GenerateNewId();
        var context = Context("Shared", targetId);

        _repository.Setup(repository => repository.GetAllById(
                It.Is<string[]>(ids =>
                    ids.Length == 1 && ids[0] == targetId.ToString()),
                It.IsAny<Dictionary<string, string>>(),
                _ct))
            .ReturnsAsync(missingFirstLevel
                ? []
                : [Result(targetId, ("Parent", parentId.ToString()))]);

        _repository.Setup(repository => repository.GetAllById(
                It.Is<string[]>(ids =>
                    ids.Length == 1 && ids[0] == parentId.ToString()),
                It.IsAny<Dictionary<string, string>>(),
                _ct))
            .ReturnsAsync([]);

        await _service.Enrich(
            _modelService.WorkflowDefinitions["SourceA"],
            [context],
            [new PropertyLookup("Shared.Parent.Name")],
            _ct,
            replaceStep: false);

        Assert.Null(context.Get("Shared.Parent.Name"));

        _repository.Verify(repository => repository.GetAllById(
            It.IsAny<string[]>(),
            It.IsAny<Dictionary<string, string>>(),
            _ct), Times.Exactly(missingFirstLevel ? 1 : 2));
    }

    [Fact]
    public async Task Enrich_AbsentNestedReference_DoesNotQueryNextLevel()
    {
        var targetId = ObjectId.GenerateNewId();
        var context = Context("Shared", targetId);

        _repository.Setup(repository => repository.GetAllById(
                It.Is<string[]>(ids =>
                    ids.Length == 1 && ids[0] == targetId.ToString()),
                It.IsAny<Dictionary<string, string>>(),
                _ct))
            .ReturnsAsync([Result(targetId)]);

        await _service.Enrich(
            _modelService.WorkflowDefinitions["SourceA"],
            [context],
            [new PropertyLookup("Shared.Parent.Name")],
            _ct,
            replaceStep: false);

        Assert.Null(context.Get("Shared.Parent.Name"));

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
                                        - name: Parent
                                          type: TargetA
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