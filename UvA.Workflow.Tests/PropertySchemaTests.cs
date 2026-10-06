using NJsonSchema;
using UvA.Workflow.SchemaGenerator.Generation;

namespace UvA.Workflow.Tests;

public class PropertySchemaTests
{
    [Theory]
    [InlineData("Date", "")]
    [InlineData("Date!", "")]
    [InlineData("[Date]", "")]
    [InlineData("[Date]!", "")]
    [InlineData("[File]!", ", \"fileSettings\": {\"allowedTypes\": [\"zip\"], \"maximumSize\": 1000}")]
    [InlineData("String", ", \"layout\": {\"multiline\": true, \"variant\": \"Email\"}")]
    [InlineData("User", ", \"allowsExternalUsers\": true")]
    [InlineData("Country", ", \"values\": [{\"name\": \"NL\"}], \"layout\": {\"type\": \"Dropdown\"}")]
    [InlineData("[Context]", ", \"inheritedRoles\": [\"Coordinator\"], \"layout\": {\"type\": \"RadioList\"}")]
    [InlineData("Assessment", ", \"layout\": {\"type\": \"Modal\"}")]
    [InlineData("Country", "")]
    public async Task AcceptsExistingTypeSyntaxAndMatchingSettings(string type, string settings)
    {
        var schema = await Generate();
        Assert.Empty(schema.Validate($"{{\"name\": \"Value\", \"type\": \"{type}\"{settings}}}"));
    }

    [Theory]
    [InlineData("Date", "\"fileSettings\": {\"allowedTypes\": [\"pdf\"]}")]
    [InlineData("Date", "\"allowsExternalUsers\": true")]
    [InlineData("Date", "\"layout\": {\"multiline\": true}")]
    [InlineData("File", "\"layout\": {\"type\": \"Dropdown\"}")]
    [InlineData("String", "\"layout\": {\"type\": \"RadioList\"}")]
    [InlineData("String", "\"values\": [{\"name\": \"NL\"}]")]
    [InlineData("Country", "\"fileSettings\": {}")]
    [InlineData("Country", "\"values\": [], \"filter\": \"Title\"")]
    [InlineData("Assessment", "\"layout\": {\"type\": \"Modal\"}, \"inheritedRoles\": []")]
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
        Assert.DoesNotContain("fileSettings", date.Properties.Keys);
        Assert.Equal(["name", "type"], date.RequiredProperties);

        var text = schema.Definitions[nameof(StringPropertyDefinition)];
        Assert.Contains(text.Properties["layout"].OneOf,
            branch => branch.Reference == schema.Definitions[nameof(TextLayoutOptions)]);
    }

    private static async Task<JsonSchema> Generate()
    {
        var documentation = new DocumentationReader();
        await documentation.Load(CancellationToken.None);
        return new Generator(documentation).Generate(typeof(PropertyDefinition));
    }
}