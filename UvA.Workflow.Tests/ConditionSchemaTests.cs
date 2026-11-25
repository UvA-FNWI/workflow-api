using NJsonSchema;
using UvA.Workflow.SchemaGenerator.Generation;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests;

public class ConditionSchemaTests
{
    [Fact]
    public async Task Step_UsesSharedRecursiveConditionSchema()
    {
        var documentation = new DocumentationReader();
        await documentation.Load(CancellationToken.None);
        var generator = new Generator(documentation, typeof(Condition), typeof(PropertyDefinition));
        var condition = generator.Generate(typeof(Condition)).ToJson();
        var property = generator.Generate(typeof(PropertyDefinition)).ToJson();
        var step = generator.Generate(typeof(Step));
        Assert.DoesNotContain(nameof(Condition), step.Definitions.Keys);
        var json = step.ToJson();
        Assert.Contains("\"$ref\": \"Condition.json\"", json);

        var directory = Directory.CreateTempSubdirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "Condition.json"), condition);
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "PropertyDefinition.json"), property);
            var resolved = await JsonSchema.FromJsonAsync(json, Path.Combine(directory.FullName, "Step.json"));

            Assert.Empty(resolved.Validate("""
                                           {"name":"Review","ends":{"and":[{"or":["HasSubmitted",{"event":"Resubmitted"}]},{"event":"Approved"}]}}
                                           """));
            Assert.Empty(resolved.Validate("""
                                           {"name":"Review","ends":{"and":[{"event":"Approved"}],"or":null}}
                                           """));
            Assert.NotEmpty(resolved.Validate("""
                                              {"name":"Review","ends":{"logical":{"operator":"Or","children":[{"event":"Approved"}]}}}
                                              """));
            Assert.NotEmpty(resolved.Validate("""
                                              {"name":"Review","ends":{"and":[{"event":"Approved"}],"or":[{"event":"Waived"}]}}
                                              """));
        }
        finally
        {
            directory.Delete(true);
        }
    }
}