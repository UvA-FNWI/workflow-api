using UvA.Workflow.Tools;

namespace UvA.Workflow.Tests;

public class TypedPropertyDefinitionTests
{
    [Theory]
    [InlineData("String", typeof(StringPropertyDefinition), DataType.String)]
    [InlineData("Date!", typeof(DatePropertyDefinition), DataType.Date)]
    [InlineData("DateTime", typeof(DateTimePropertyDefinition), DataType.DateTime)]
    [InlineData("Int", typeof(IntPropertyDefinition), DataType.Int)]
    [InlineData("Double", typeof(DoublePropertyDefinition), DataType.Double)]
    [InlineData("Check", typeof(CheckPropertyDefinition), DataType.Check)]
    [InlineData("Currency", typeof(CurrencyPropertyDefinition), DataType.Currency)]
    [InlineData("[File]!", typeof(FilePropertyDefinition), DataType.File)]
    [InlineData("[User]", typeof(UserPropertyDefinition), DataType.User)]
    public void Parser_SelectsBuiltInDefinition_FromExistingTypeSyntax(string type, Type expected, DataType dataType)
    {
        var property = ParseProperty($"name: Value\ntype: '{type}'");

        Assert.IsType(expected, property);
        Assert.Equal(dataType, property.DataType);
        Assert.Equal(type.EndsWith('!'), property.IsRequired);
        Assert.Equal(type.StartsWith('['), property.IsArray);
    }

    [Theory]
    [InlineData("Date", "fileSettings: { allowedTypes: [pdf] }")]
    [InlineData("File", "allowsExternalUsers: true")]
    [InlineData("Date", "layout: { multiline: true }")]
    [InlineData("String", "layout: { type: RadioList }")]
    [InlineData("Country", "values: [{ name: NL }]\nfilter: Title")]
    [InlineData("Date", "calculation: { weight: 1 }")]
    [InlineData("String", "calculation: { weight: 1 }")]
    [InlineData("File", "calculation: { weight: 1 }")]
    [InlineData("Check", "calculation: { weight: 1 }")]
    public void Parser_RejectsSettingsBelongingToAnotherType(string type, string settings)
        => Assert.Throws<Exception>(() => ParseProperty($"name: Value\ntype: {type}\n{settings}"));

    [Theory]
    [InlineData("Int")]
    [InlineData("Double")]
    [InlineData("Currency")]
    [InlineData("Grade")]
    public void Parser_ReadsCalculationOnWeightedProperties(string type)
    {
        var choices = type == "Grade" ? "\nvalues: [{ name: Pass, value: 7 }]" : "";
        var property = Assert.IsAssignableFrom<WeightedPropertyDefinition>(ParseProperty(
            $"name: Score\ntype: {type}\ncalculation: {{ weight: 2, type: Sum }}{choices}"));

        Assert.Equal(2m, property.Calculation!.Weight);
        Assert.Equal(CalculationType.Sum, property.Calculation.Type);
    }

    [Fact]
    public void Parser_StillResolvesNamedChoices()
    {
        var property = ParseProperty("name: Country\ntype: Country\nvalues:\n  - name: NL\n    text: Netherlands");

        Assert.Equal(DataType.Choice, property.DataType);
        Assert.Equal("NL", Assert.Single(Assert.IsType<ChoicePropertyDefinition>(property).Values!).Name);
    }

    [Fact]
    public void Parser_TypesNamedReferencesAndInheritedEmbeddedObjects()
    {
        var parser = new ModelParser(new DictionaryProvider(new Dictionary<string, string>
        {
            ["Project/Entity.yaml"] = """
                                      name: Project
                                      properties:
                                        - name: Course
                                          type: '[Context]!'
                                          inheritedRoles: [Coordinator]
                                          layout: { type: RadioList }
                                        - name: Assessment
                                          type: '[Assessment]'
                                          layout: { type: Modal }
                                      """,
            ["Context/Entity.yaml"] = "name: Context",
            ["Assessment/Entity.yaml"] = "name: Assessment\ninheritsFrom: BaseAssessment",
            ["BaseAssessment/Entity.yaml"] = "name: BaseAssessment\nisEmbedded: true"
        }));

        var properties = parser.WorkflowDefinitions["Project"].Properties;
        var reference = Assert.IsType<ReferencePropertyDefinition>(properties.Get("Course"));
        Assert.Equal(DataType.Reference, reference.DataType);
        Assert.True(reference.IsArray);
        Assert.True(reference.IsRequired);
        Assert.Same(parser.WorkflowDefinitions["Context"], reference.WorkflowDefinition);
        Assert.Equal("Coordinator", Assert.Single(reference.InheritedRoles));
        Assert.Equal(ChoiceLayoutType.RadioList, reference.Layout!.Type);

        var embedded = Assert.IsType<ObjectPropertyDefinition>(properties.Get("Assessment"));
        Assert.Equal(DataType.Object, embedded.DataType);
        Assert.Same(parser.WorkflowDefinitions["Assessment"], embedded.WorkflowDefinition);
        Assert.Equal(TableLayout.Modal, embedded.Layout!.Type);
    }

    [Fact]
    public void Parser_ReadsTextAndChoiceLayoutsAsTypedSettings()
    {
        var text = Assert.IsType<StringPropertyDefinition>(ParseProperty(
            "name: Notes\ntype: String\nlayout: { multiline: true, allowAttachments: true }"));
        Assert.True(text.Layout!.Multiline);
        Assert.True(text.Layout.AllowAttachments);
        Assert.Null(text.Layout.Variant);

        var choice = Assert.IsType<ChoicePropertyDefinition>(ParseProperty(
            "name: Country\ntype: Country\nvalues: [{ name: NL }]\nlayout: { type: RadioList }"));
        Assert.Equal(ChoiceLayoutType.RadioList, choice.Layout!.Type);

        var defaultLayout = Assert.IsType<ChoicePropertyDefinition>(ParseProperty(
            "name: Country\ntype: Country\nvalues: [{ name: NL }]\nlayout: {}"));
        Assert.Null(defaultLayout.Layout!.Type);
    }

    private static PropertyDefinition ParseProperty(string yaml)
    {
        var content = "name: Project\ntitlePlural: Projects\nproperties:\n  - " + yaml.Replace("\n", "\n    ");
        var parser = new ModelParser(new DictionaryProvider(new Dictionary<string, string>
        {
            ["Project/Entity.yaml"] = content
        }));
        return Assert.Single(parser.WorkflowDefinitions["Project"].Properties);
    }
}