using MongoDB.Bson;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests;

public class ValueSetExpressionTests
{
    [Theory]
    [InlineData("ExpressionConditions", false)]
    [InlineData("ExpressionConditions", true)]
    [InlineData("InheritedExpressionConditions", false)]
    [InlineData("InheritedExpressionConditions", true)]
    public void Except_FixtureResolvesCommonLocalAndInheritedValueSets(string workflow, bool fullInstance)
    {
        var model = new ModelService(UnitTestsHelpers.CreateModelParser());
        var definition = model.WorkflowDefinitions[workflow];
        var condition = definition.Properties.Single(p => p.Name == "WithinSetLimit").Condition!;
        var data = new Dictionary<string, BsonValue>
        {
            ["Selection"] = new BsonArray(new[] { "None", "Alpha", "Beta", "Gamma" }),
            ["Limit"] = 1
        };
        var context = fullInstance
            ? model.CreateContext(
                new WorkflowInstance { WorkflowDefinition = workflow, Properties = data, Events = [] })
            : ObjectContext.Create(definition, data);

        Assert.True(condition.IsMet(context));
        context.Values["Limit"] = 0;
        Assert.False(condition.IsMet(context));
    }

    [Theory]
    [InlineData("Title")]
    [InlineData("now")]
    [InlineData("Id")]
    [InlineData("CurrentStep")]
    [InlineData("CreateDate")]
    [InlineData("LastEvent")]
    [InlineData("UploadEvent")]
    [InlineData("UploadEventActive")]
    [InlineData("Access")]
    public void ValueSetName_MayMatchPropertyContextAndEffectNames(string name)
    {
        var definition = new ModelParser(new DictionaryProvider(new()
        {
            ["Example/Entity.yaml"] = """
                                      name: Example
                                      titlePlural: Examples
                                      events:
                                        - name: Upload
                                      properties:
                                        - name: Title
                                          type: String
                                      """,
            [$"Example/ValueSets/{name}.yaml"] = $"name: {name}\nvalues:\n  - name: None"
        })).WorkflowDefinitions["Example"];
        var context = ObjectContext.Create(definition, new Dictionary<string, BsonValue> { ["Title"] = "My title" });

        Assert.Equal(new[] { "None" }, context.Get($"ValueSets.{name}"));
        Assert.Equal("My title", context.Get("Title"));
    }

    [Theory]
    [InlineData("Base")]
    [InlineData("Child")]
    public void PropertyNamedValueSets_IsRejected(string declaredIn)
    {
        const string property = "\nproperties:\n  - name: ValueSets\n    type: String";
        var error = Assert.Throws<Exception>(() => new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = "name: Base\ntitlePlural: Bases" + (declaredIn == "Base" ? property : ""),
            ["Child/Entity.yaml"] = "name: Child\ntitlePlural: Children\ninheritsFrom: Base" +
                                    (declaredIn == "Child" ? property : "")
        })));

        Assert.Contains("'ValueSets' is reserved", error.Message);
        Assert.Contains($"'{declaredIn}'", error.Message);
    }
}