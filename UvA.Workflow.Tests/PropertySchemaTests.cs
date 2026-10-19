using NJsonSchema;
using UvA.Workflow.SchemaGenerator.Generation;

namespace UvA.Workflow.Tests;

public class PropertySchemaTests
{
    [Theory]
    [InlineData(typeof(WorkflowDefinition))]
    [InlineData(typeof(Step))]
    public async Task ReferencesSharedPropertySchema(Type type)
    {
        var documentation = new DocumentationReader();
        await documentation.Load(CancellationToken.None);
        var generator = new Generator(documentation, typeof(PropertyDefinition));
        var shared = generator.Generate(typeof(PropertyDefinition)).ToJson();
        var schema = generator.Generate(type);
        Assert.DoesNotContain(schema.Definitions.Keys, name => name.EndsWith("PropertyDefinition"));
        var json = schema.ToJson();
        Assert.Contains("\"$ref\": \"PropertyDefinition.json\"", json);

        var directory = Directory.CreateTempSubdirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "PropertyDefinition.json"), shared);
            var resolved = await JsonSchema.FromJsonAsync(json, Path.Combine(directory.FullName, $"{type.Name}.json"));
            var property = resolved.Properties["properties"].Item;
            Assert.NotNull(property);
            Assert.Empty(property.Validate("{\"name\": \"Due\", \"type\": \"Date\"}"));
            Assert.Empty(property.Validate("{\"name\": \"Notes\", \"type\": \"String\", \"variant\": null}"));
            Assert.Empty(
                property.Validate("{\"name\": \"Notes\", \"type\": \"String\", \"multiline\": true}"));
            Assert.NotEmpty(property.Validate("{\"name\": \"Due\", \"type\": \"Date\", \"allowedTypes\": [\"pdf\"]}"));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData("Date", "")]
    [InlineData("Date!", "")]
    [InlineData("[Date]", "")]
    [InlineData("[Date]!", "")]
    [InlineData("File", "")]
    [InlineData("File", ", \"allowedTypes\": [\"zip\"]")]
    [InlineData("File", ", \"maximumSize\": 1000")]
    [InlineData("[File]!", ", \"allowedTypes\": [\"zip\"], \"maximumSize\": 1000, \"prefix\": \"archive_\"")]
    [InlineData("String", ", \"multiline\": true, \"allowAttachments\": true, \"variant\": \"Email\"")]
    [InlineData("User", ", \"allowsExternalUsers\": true")]
    [InlineData("Country", ", \"values\": [{\"name\": \"NL\"}], \"layout\": \"Dropdown\"")]
    [InlineData("[Context]", ", \"inheritedRoles\": [\"Coordinator\"], \"layout\": \"RadioList\"")]
    [InlineData("Assessment", ", \"layout\": \"Modal\"")]
    [InlineData("Country", ", \"layout\": null")]
    [InlineData("Country", "")]
    [InlineData("Int!", ", \"calculation\": {\"weight\": 2}")]
    [InlineData("Double", ", \"calculation\": {\"weight\": 2}")]
    [InlineData("Currency", ", \"calculation\": {\"type\": \"Sum\"}")]
    [InlineData("Grade", ", \"calculation\": {\"weight\": 2}")]
    public async Task AcceptsExistingTypeSyntaxAndMatchingSettings(string type, string settings)
    {
        var schema = await Generate();
        Assert.Empty(schema.Validate($"{{\"name\": \"Value\", \"type\": \"{type}\"{settings}}}"));
    }

    [Theory]
    [InlineData("Date", "\"allowedTypes\": [\"pdf\"]")]
    [InlineData("Date", "\"allowsExternalUsers\": true")]
    [InlineData("Date", "\"multiline\": true")]
    [InlineData("File", "\"layout\": \"Dropdown\"")]
    [InlineData("String", "\"layout\": \"RadioList\"")]
    [InlineData("String", "\"layout\": {\"multiline\": true}")]
    [InlineData("Country", "\"values\": [], \"layout\": {\"type\": \"RadioList\"}")]
    [InlineData("Country", "\"values\": [], \"multiline\": true")]
    [InlineData("Country", "\"values\": [], \"variant\": \"Email\"")]
    [InlineData("String", "\"values\": [{\"name\": \"NL\"}]")]
    [InlineData("Country", "\"maximumSize\": 1000")]
    [InlineData("String", "\"prefix\": \"report_\"")]
    [InlineData("File", "\"allowedTypes\": null")]
    [InlineData("File", "\"maximumSize\": null")]
    [InlineData("File", "\"fileSettings\": {\"allowedTypes\": [\"pdf\"]}")]
    [InlineData("Country", "\"values\": [], \"filter\": \"Title\"")]
    [InlineData("Assessment", "\"layout\": \"Modal\", \"inheritedRoles\": []")]
    [InlineData("Date", "\"calculation\": {\"weight\": 1}")]
    [InlineData("String", "\"calculation\": {\"weight\": 1}")]
    [InlineData("File", "\"calculation\": {\"weight\": 1}")]
    [InlineData("Check", "\"calculation\": {\"weight\": 1}")]
    public async Task RejectsSettingsBelongingToAnotherType(string type, string settings)
    {
        var schema = await Generate();
        Assert.NotEmpty(schema.Validate($"{{\"name\": \"Value\", \"type\": \"{type}\", {settings}}}"));
    }

    [Fact]
    public async Task PropertyUnion_HasClosedVariantsAndTypeSpecificLayouts()
    {
        var schema = await Generate();
        Assert.Equal(12, schema.AnyOf.Count);
        var date = schema.Definitions[nameof(DatePropertyDefinition)];
        Assert.False(date.AllowAdditionalProperties);
        Assert.Equal(["Date", "Date!", "[Date]", "[Date]!"], date.Properties["type"].Enumeration);
        Assert.DoesNotContain("allowedTypes", date.Properties.Keys);
        var file = schema.Definitions[nameof(FilePropertyDefinition)];
        Assert.DoesNotContain("fileSettings", file.Properties.Keys);
        Assert.Contains("prefix", file.Properties.Keys);
        Assert.Contains("allowedTypes", file.Properties.Keys);
        Assert.Contains("maximumSize", file.Properties.Keys);
        Assert.DoesNotContain("allowedTypes", file.RequiredProperties);
        Assert.DoesNotContain("maximumSize", file.RequiredProperties);
        Assert.Equal(new FilePropertyDefinition().MaximumSize, file.Properties["maximumSize"].Default);
        Assert.Equal(["name", "type"], date.RequiredProperties);

        var text = schema.Definitions[nameof(StringPropertyDefinition)];
        Assert.DoesNotContain("layout", text.Properties.Keys);
        Assert.Contains("multiline", text.Properties.Keys);
        Assert.Contains("allowAttachments", text.Properties.Keys);
        Assert.Contains("variant", text.Properties.Keys);
        Assert.Contains(schema.Definitions[nameof(ChoicePropertyDefinition)].Properties["layout"].OneOf,
            branch => branch.Reference == schema.Definitions[nameof(ChoiceLayoutType)]);
        Assert.Contains(schema.Definitions[nameof(ObjectPropertyDefinition)].Properties["layout"].OneOf,
            branch => branch.Reference == schema.Definitions[nameof(TableLayout)]);
    }

    private static async Task<JsonSchema> Generate()
    {
        var documentation = new DocumentationReader();
        await documentation.Load(CancellationToken.None);
        return new Generator(documentation).Generate(typeof(PropertyDefinition));
    }
}