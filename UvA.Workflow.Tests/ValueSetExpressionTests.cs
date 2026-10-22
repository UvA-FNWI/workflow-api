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
    [InlineData("Common")]
    [InlineData("Base")]
    [InlineData("Child")]
    public void ValueSetName_CannotMatchAPropertyIncludingInheritedProperties(string scope)
    {
        var error = Assert.Throws<Exception>(() => new ModelParser(new DictionaryProvider(new()
        {
            ["Base/Entity.yaml"] = """
                                   name: Base
                                   titlePlural: Bases
                                   properties:
                                     - name: ExcludedOptions
                                       type: String
                                   """,
            ["Child/Entity.yaml"] = "name: Child\ntitlePlural: Children\ninheritsFrom: Base",
            [$"{scope}/ValueSets/ExcludedOptions.yaml"] = "name: ExcludedOptions\nvalues:\n  - name: None"
        })));

        Assert.Contains("ExcludedOptions", error.Message);
        Assert.Contains("conflicts", error.Message);
    }

    [Theory]
    [InlineData("now")]
    [InlineData("Id")]
    [InlineData("CurrentStep")]
    [InlineData("CreateDate")]
    [InlineData("LastEvent")]
    [InlineData("UploadEvent")]
    [InlineData("UploadEventActive")]
    public void ValueSetName_CannotMatchAContextName(string name)
    {
        var error = Assert.Throws<Exception>(() => new ModelParser(new DictionaryProvider(new()
        {
            ["Example/Entity.yaml"] = "name: Example\ntitlePlural: Examples\nevents:\n  - name: Upload",
            ["Example/ValueSets/ExcludedOptions.yaml"] = $"name: {name}\nvalues:\n  - name: None"
        })));

        Assert.Contains(name, error.Message);
        Assert.Contains("conflicts", error.Message);
    }
}