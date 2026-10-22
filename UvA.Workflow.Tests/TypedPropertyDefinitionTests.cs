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
    [InlineData("Date", "allowedTypes: [pdf]")]
    [InlineData("String", "maximumSize: 1000")]
    [InlineData("Date", "prefix: report_")]
    [InlineData("File", "allowsExternalUsers: true")]
    [InlineData("Date", "control: TextArea")]
    [InlineData("String", "allowAttachments: true")]
    [InlineData("String", "multiline: true")]
    [InlineData("String", "variant: Email")]
    [InlineData("String", "layout: RadioList")]
    [InlineData("String", "control: RadioList")]
    [InlineData("Country", "values: [{ name: NL }]\ncontrol: Email")]
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
                                          control: RadioList
                                        - name: Assessment
                                          type: '[Assessment]'
                                          control: Modal
                                      """,
            ["Context/Entity.yaml"] = """
                                      name: Context
                                      properties:
                                        - name: Coordinator
                                          type: User
                                          isInheritableRole: true
                                      """,
            ["Assessment/Entity.yaml"] = "name: Assessment\ninheritsFrom: BaseAssessment",
            ["BaseAssessment/Entity.yaml"] = "name: BaseAssessment\nisEmbedded: true"
        }));

        var properties = parser.WorkflowDefinitions["Project"].Properties;
        var reference = Assert.IsType<ReferencePropertyDefinition>(properties.Get("Course"));
        Assert.Equal(DataType.Reference, reference.DataType);
        Assert.True(reference.IsArray);
        Assert.True(reference.IsRequired);
        Assert.Same(parser.WorkflowDefinitions["Context"], reference.WorkflowDefinition);
        Assert.Equal(ChoiceControl.RadioList, reference.Control);

        Assert.NotNull(reference.WorkflowDefinition);
        var coordinator = Assert.IsType<UserPropertyDefinition>(
            reference.WorkflowDefinition.Properties.Get("Coordinator"));
        Assert.True(coordinator.IsInheritableRole);
        Assert.DoesNotContain(properties, property => property.Name == "Coordinator");

        var embedded = Assert.IsType<ObjectPropertyDefinition>(properties.Get("Assessment"));
        Assert.Equal(DataType.Object, embedded.DataType);
        Assert.Same(parser.WorkflowDefinitions["Assessment"], embedded.WorkflowDefinition);
        Assert.Equal(ObjectControl.Modal, embedded.Control);
    }

    [Fact]
    public void Parser_ReadsTextAndChoiceControls()
    {
        var text = Assert.IsType<StringPropertyDefinition>(ParseProperty(
            "name: Notes\ntype: String\ncontrol: TextArea"));
        Assert.Equal(StringControl.TextArea, text.Control);

        var choice = Assert.IsType<ChoicePropertyDefinition>(ParseProperty(
            "name: Country\ntype: Country\nvalues: [{ name: NL }]\ncontrol: RadioList"));
        Assert.Equal(ChoiceControl.RadioList, choice.Control);

        var defaultLayout = Assert.IsType<ChoicePropertyDefinition>(ParseProperty(
            "name: Country\ntype: Country\nvalues: [{ name: NL }]"));
        Assert.Null(defaultLayout.Control);
    }

    [Fact]
    public void Parser_UsesFileDefaultsWhenLimitsAreOmitted()
    {
        var file = Assert.IsType<FilePropertyDefinition>(ParseProperty("name: Report\ntype: File"));

        Assert.Equal(["pdf"], file.AllowedTypes);
        Assert.Equal(10_000_000, file.MaximumSize);
    }

    [Theory]
    [InlineData("allowedTypes: null")]
    [InlineData("allowedTypes: []")]
    [InlineData("maximumSize: null")]
    [InlineData("maximumSize: 0")]
    [InlineData("maximumSize: -1")]
    public void Parser_RejectsInvalidFileLimits(string settings)
        => Assert.Throws<Exception>(() => ParseProperty($"name: Report\ntype: File\n{settings}"));

    [Fact]
    public void Parser_ReadsFlattenedFileConstraintsAndPrefix()
    {
        var file = Assert.IsType<FilePropertyDefinition>(ParseProperty(
            "name: Report\ntype: File\nallowedTypes: [' .PDF ', pdf, '.ZIP']\nmaximumSize: 1000\nprefix: report_"));

        Assert.Equal(["pdf", "zip"], file.AllowedTypes);
        Assert.Equal(1000, file.MaximumSize);
        Assert.Equal("report_", file.Prefix);
        Assert.NotNull(file.PrefixTemplate);
    }

    [Theory]
    [InlineData("String", "control: { multiline: true }")]
    [InlineData("Country", "values: [{ name: NL }]\ncontrol: { type: RadioList }")]
    [InlineData("File", "fileSettings: { allowedTypes: [pdf] }")]
    public void Parser_RejectsNestedPropertySettings(string type, string settings)
        => Assert.Throws<Exception>(() => ParseProperty($"name: Value\ntype: {type}\n{settings}"));

    [Theory]
    [InlineData("TextArea", StringControl.TextArea)]
    [InlineData("Email", StringControl.Email)]
    [InlineData("Phone", StringControl.Phone)]
    public void Parser_ReadsTextControl(string control, StringControl expected)
    {
        var property = Assert.IsType<StringPropertyDefinition>(ParseProperty(
            $"name: Contact\ntype: String\ncontrol: {control}"));

        Assert.Equal(expected, property.Control);
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