using MongoDB.Bson;
using UvA.Workflow.Expressions;
using UvA.Workflow.Tools;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel.Conditions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UvA.Workflow.Tests;

public class ListConditionTests
{
    [Theory]
    [InlineData("None", false)]
    [InlineData("None,First", false)]
    [InlineData("None,First,Second", true)]
    public void YamlCondition_CountsChoicesExceptLiteral(string selected, bool expected)
    {
        var model = CreateModel();
        var instance = Instance(selected.Split(','));
        var condition = model.WorkflowDefinitions["Selection"].Properties.Get("Details").Condition;

        Assert.Equal(expected, condition.IsMet(model.CreateContext(instance)));
        Assert.Equal([new PropertyLookup("Selected")], condition!.Properties);
        Assert.Equal([new PropertyLookup("Selected")], condition.Part.Dependants);
        Assert.Contains(model.WorkflowDefinitions["Selection"].Properties.Get("Details"),
            model.WorkflowDefinitions["Selection"].Properties.Get("Selected").DependentQuestions);
    }

    [Theory]
    [InlineData("CommonIgnored", "Selection")]
    [InlineData("LocalIgnored", "Selection")]
    [InlineData("LocalIgnored", "DerivedSelection")]
    public void Except_ResolvesValueSetChoiceNamesInFullAndProjectedContexts(string setName, string definition)
    {
        var model = CreateModel();
        var instance = Instance(["None", "Skipped", "First", "First", "Second"], definition);
        var expression = ExpressionParser.Parse($"except(Selected, {setName})");
        var fullContext = model.CreateContext(instance);
        var projectedContext = model.CreateContext(definition, instance.Properties);

        foreach (var context in new[] { fullContext, projectedContext })
        {
            Assert.Equal(new object[] { "First", "First", "Second" },
                Assert.IsType<object[]>(expression.Execute(context)));
            Assert.Equal(3, ExpressionParser.Parse($"count(except(Selected, {setName}))").Execute(context));
        }

        Assert.Equal(5, instance.Properties["Selected"].AsBsonArray.Count);
    }

    [Theory]
    [InlineData("greaterThanOrEqual", "2", false)]
    [InlineData("greaterThanOrEqual", "1", true)]
    [InlineData("lessThanOrEqual", "0", false)]
    [InlineData("lessThanOrEqual", "1", true)]
    [InlineData("lessThanOrEqual", "2.5", true)]
    public void YamlComparison_UsesNumericExpressionResult(string comparison, string threshold, bool expected)
    {
        var condition = ParseCondition($"""
                                        value:
                                          property: count(except(Selected, =None))
                                          {comparison}: "{threshold}"
                                        """);
        var context = CreateModel().CreateContext(Instance(["None", "First"]));

        Assert.Equal(expected, condition.IsMet(context));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData(new string[] { }, true)]
    [InlineData(new[] { "None" }, false)]
    [InlineData(new[] { "" }, false)]
    [InlineData(0, false)]
    public void IsEmpty_HandlesNullStringsAndCollections(object? value, bool expected)
    {
        var context = new ObjectContext(new Dictionary<Lookup, object?> { ["Selected"] = value });

        Assert.Equal(expected, ParseCondition("value:\n  property: Selected\n  isEmpty: true").IsMet(context));
        Assert.Equal(!expected, ParseCondition("value:\n  property: Selected\n  isEmpty: false").IsMet(context));
    }

    [Fact]
    public void IsEmpty_HandlesMultiSelectFromStoredInstance()
    {
        var model = CreateModel();
        var condition = ParseCondition("value:\n  property: Selected\n  isEmpty: true");

        Assert.True(condition.IsMet(model.CreateContext(Instance([]))));
        Assert.False(condition.IsMet(model.CreateContext(Instance(["First"]))));
    }

    private static Condition ParseCondition(string yaml) => new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance).Build().Deserialize<Condition>(yaml);

    private static WorkflowInstance Instance(string[] choices, string definition = "Selection") => new()
    {
        WorkflowDefinition = definition,
        Properties = new Dictionary<string, BsonValue> { ["Selected"] = new BsonArray(choices) },
        Events = []
    };

    private static ModelService CreateModel() => new(new ModelParser(new DictionaryProvider(new()
    {
        ["Selection/Entity.yaml"] = """
                                    name: Selection
                                    properties:
                                      - name: Selected
                                        type: "[Choice]"
                                        values:
                                          - name: None
                                          - name: Skipped
                                          - name: First
                                          - name: Second
                                      - name: Details
                                        type: String
                                        condition:
                                          value:
                                            property: count(except(Selected, =None))
                                            greaterThanOrEqual: "2"
                                    """,
        ["DerivedSelection/Entity.yaml"] = "name: DerivedSelection\ninheritsFrom: Selection",
        ["Common/ValueSets/CommonIgnored.yaml"] = """
                                                  name: CommonIgnored
                                                  values:
                                                    - name: None
                                                      text: No choice
                                                    - name: Skipped
                                                      text: Skip this choice
                                                  """,
        ["Selection/ValueSets/LocalIgnored.yaml"] = """
                                                    name: LocalIgnored
                                                    values:
                                                      - name: None
                                                      - name: Skipped
                                                    """
    })));
}