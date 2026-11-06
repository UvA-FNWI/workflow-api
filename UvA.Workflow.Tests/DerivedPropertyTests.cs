using MongoDB.Bson;
using Moq;
using UvA.Workflow.Events;
using UvA.Workflow.Expressions;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tools;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests;

public class DerivedPropertyTests
{
    private const string Properties = """
                                      name: Project
                                      properties:
                                        - name: HasParties
                                          type: Check
                                          expression: contains(Countries, =NL)
                                        - name: Count
                                          type: Int
                                          expression: count(Parties)
                                        - name: Countries
                                          type: '[String]'
                                          expression: unique(flatten(select(Parties, =Country)))
                                        - name: Parties
                                          type: '[Party]'
                                      """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Context_ComputesTypedDependenciesBeforeConditionsAndTemplates(bool projected)
    {
        var model = Parse();
        var instance = Instance();
        var context = projected
            ? model.CreateContext("Project", instance.Properties)
            : model.CreateContext(instance);

        Assert.Equal(2, context.Get("Count"));
        Assert.Equal(["NL", "BE"], Assert.IsType<string[]>(context.Get("Countries")));
        Assert.True(new Condition { Value = new Value { Property = "HasParties", Equal = "true" } }
            .IsMet(context));
        Assert.Equal("Parties: 2", Template.Create("Parties: {{ Count }}").Apply(context));
        Assert.DoesNotContain("Count", instance.Properties.Keys);
        Assert.DoesNotContain("Countries", instance.Properties.Keys);
    }

    [Fact]
    public void RebuildingContext_UsesChangedSourcesAndIgnoresOldStoredComputedValue()
    {
        var model = Parse();
        var instance = Instance();
        instance.Properties["Count"] = 99;
        Assert.Equal(2, model.CreateContext(instance).Get("Count"));
        instance.Properties["Parties"] = new BsonArray();
        Assert.Equal(0, model.CreateContext(instance).Get("Count"));
        Assert.Equal(99, instance.Properties["Count"].AsInt32);
    }

    [Theory]
    [InlineData("Int", "=text")]
    [InlineData("Int", "2.5")]
    [InlineData("[Int]", "count(Parties)")]
    [InlineData("[Int]", "Countries")]
    [InlineData("String", "Parties")]
    public void Context_RejectsResultOfWrongDeclaredType(string type, string expression)
    {
        var model = Parse(Properties + $"\n  - name: Invalid\n    type: '{type}'\n    expression: {expression}");
        var exception = Assert.ThrowsAny<Exception>(() => model.CreateContext(Instance()));
        Assert.Contains("Invalid", exception.Message);
        Assert.Contains(type, exception.Message);
    }

    [Theory]
    [InlineData("expression: 'count('", "Invalid expression")]
    [InlineData("expression: ' '", "Invalid expression")]
    [InlineData("expression: 'unknown(Other)'", "Invalid expression")]
    [InlineData("expression: 'count(Other, Other)'", "Invalid expression")]
    [InlineData("expression: 'count(Other))'", "Invalid expression")]
    [InlineData("expression: Missing", "Invalid expression")]
    [InlineData("expression: Parties + 1", "Invalid expression")]
    [InlineData("expression: 1\n    default: 2", "default")]
    [InlineData("expression: 1\n    default: ''", "default")]
    [InlineData("expression: Other", "cycle")]
    public void Parser_RejectsInvalidComputedConfiguration(string settings, string message)
    {
        var yaml = "name: Project\nproperties:\n  - name: Value\n    type: Int\n    " + settings +
                   "\n  - name: Other\n    type: Int\n    expression: Value";
        var exception = Assert.ThrowsAny<Exception>(() => Parse(yaml));
        Assert.Contains(message, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Project/Forms/Edit.yaml",
        "name: Edit\npages:\n  - name: Main\n    elements:\n      - question: Count")]
    [InlineData("Project/Screens/List.yaml", "name: List\ncolumns: []\nbulkEdit:\n  editableProperties: [Count]")]
    [InlineData("Project/Screens/List.yaml", "name: List\ncolumns: []\nbulkEdit:\n  identifier:\n    property: Count")]
    [InlineData("Project/Forms/Edit.yaml",
        "name: Edit\nonSubmit:\n  - setProperty:\n      property: Count\n      value: 1")]
    public void Parser_RejectsWritableUsesOfComputedProperty(string path, string content)
    {
        var exception =
            Assert.ThrowsAny<Exception>(() => Parse(extra: new Dictionary<string, string> { [path] = content }));
        Assert.Contains("Count", exception.Message);
        Assert.Contains("computed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectedContext_DoesNotComputeWhenSourceWasNotProjected()
    {
        var context = Parse().CreateContext("Project", new Dictionary<string, BsonValue> { ["_id"] = "id" });
        Assert.Null(context.Get("Count"));
        Assert.Null(context.Get("HasParties"));
    }

    [Fact]
    public void Conditions_RegisterDependencyOnComputedSources()
    {
        var model = Parse(Properties +
                          "\n  - name: Detail\n    type: String\n    condition:\n      value:\n        property: HasParties\n        equal: true");
        Assert.Contains(model.WorkflowDefinitions["Project"].Properties.Get("Parties").DependentQuestions,
            question => question.Name == "Detail");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Context_ComputesPropertiesInsideEmbeddedObjects(bool array)
    {
        var model = Parse("name: Project\nproperties:\n  - name: Nested\n    type: '" +
                          (array ? "[Party]" : "Party") + "'", new Dictionary<string, string>
        {
            ["Party/Entity.yaml"] =
                "name: Party\nisEmbedded: true\nproperties:\n  - name: Names\n    type: '[String]'\n  - name: Total\n    type: Int\n    expression: count(Names)"
        });
        var nested = new BsonDocument("Names", new BsonArray { "A", "B" });
        var context = model.CreateContext("Project",
            new Dictionary<string, BsonValue> { ["Nested"] = array ? new BsonArray { nested } : nested });
        if (array)
            Assert.Equal(2, Assert.IsType<object[]>(context.Get("Nested.Total"))[0]);
        else
            Assert.Equal(2, context.Get("Nested.Total"));
        Assert.False(nested.Contains("Total"));
    }

    [Fact]
    public void Context_ComputesPropertiesBelowIntermediateEmbeddedObject()
    {
        var model = Parse("name: Project\nproperties:\n  - name: Container\n    type: Container", new()
        {
            ["Container/Entity.yaml"] =
                "name: Container\nisEmbedded: true\nproperties:\n  - name: Party\n    type: Party",
            ["Party/Entity.yaml"] =
                "name: Party\nisEmbedded: true\nproperties:\n  - name: Names\n    type: '[String]'\n  - name: Total\n    type: Int\n    expression: count(Names)"
        });
        var context = model.CreateContext("Project", new()
        {
            ["Container"] = new BsonDocument("Party", new BsonDocument("Names", new BsonArray { "A", "B" }))
        });
        Assert.Equal(2, context.Get("Container.Party.Total"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task Answer_EmbeddedValuesRoundTripWithoutComputedDescendants(bool array, bool storedAsArray)
    {
        var model = Parse("name: Project\nproperties:\n  - name: Nested\n    type: '" +
                          (array ? "[Party]" : "Party") + "'", new()
        {
            ["Party/Entity.yaml"] =
                "name: Party\nisEmbedded: true\nproperties:\n  - name: Children\n    type: '[Child]'\n  - name: Total\n    type: Int\n    expression: count(Children)",
            ["Child/Entity.yaml"] =
                "name: Child\nisEmbedded: true\nproperties:\n  - name: Name\n    type: String\n  - name: Display\n    type: String\n    expression: Name"
        });
        var property = model.WorkflowDefinitions["Project"].Properties.Single();
        var stored = new BsonDocument
        {
            ["Children"] = new BsonArray { new BsonDocument { ["Name"] = "A", ["Display"] = "stale" } },
            ["Total"] = 99
        };
        BsonValue value = storedAsArray ? new BsonArray { stored } : stored;

        var payload = Answer.GetValue(property, value)!.Value;
        var item = array ? payload[0] : payload;
        Assert.False(item.TryGetProperty("Total", out _));
        Assert.False(item.GetProperty("Children")[0].TryGetProperty("Display", out _));
        var result = await new AnswerConversionService(Mock.Of<IUserService>(), Mock.Of<IUserRepository>())
            .ConvertToValue(payload, property, default);

        var expected = new BsonDocument("Children", new BsonArray { new BsonDocument("Name", "A") });
        Assert.Equal(array ? new BsonArray { expected } : (BsonValue)expected, result);
        Assert.Equal(99, stored["Total"].AsInt32);
    }

    [Fact]
    public void Answer_DoesNotEvaluateRequiredComputedChildren()
    {
        var model = Parse("name: Project\nproperties:\n  - name: Nested\n    type: Party", new()
        {
            ["Party/Entity.yaml"] =
                "name: Party\nisEmbedded: true\nproperties:\n  - name: Name\n    type: String\n  - name: Display\n    type: String!\n    expression: Name"
        });

        var payload = Answer.GetValue(model.WorkflowDefinitions["Project"].Properties.Single(), new BsonDocument());

        Assert.Equal("{}", payload!.Value.GetRawText());
    }

    [Fact]
    public void Parser_RejectsComputedExternalAccountTarget()
    {
        var exception = Assert.ThrowsAny<Exception>(() => Parse("""
                                                                name: Project
                                                                properties:
                                                                  - name: Owner
                                                                    type: User
                                                                  - name: Recipient
                                                                    type: User
                                                                    expression: Owner
                                                                """, new()
        {
            ["Project/Forms/Edit.yaml"] = "name: Edit\nonSubmit:\n  - createExternalUserAccount:\n      role: Recipient"
        }));
        Assert.Contains("computed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Recipient", exception.Message);
    }

    [Fact]
    public void Parser_RejectsComputedSetPropertyTargetAcrossUnprocessedEmbeddedDefinitions()
    {
        var exception = Assert.ThrowsAny<Exception>(() => Parse(
            "name: Project\nproperties:\n  - name: Container\n    type: Container", new()
            {
                ["Container/Entity.yaml"] =
                    "name: Container\nisEmbedded: true\nproperties:\n  - name: Party\n    type: Party",
                ["Party/Entity.yaml"] =
                    "name: Party\nisEmbedded: true\nproperties:\n  - name: Total\n    type: Int\n    expression: 1",
                ["Project/Forms/Edit.yaml"] =
                    "name: Edit\nonSubmit:\n  - setProperty:\n      property: Container.Party.Total\n      value: 2"
            }));
        Assert.Contains("computed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Container.Party.Total", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Context_ComputesFromConfiguredEventThatHasNotOccurred(bool projected)
    {
        var model = Parse(
            "name: Project\nevents:\n  - name: Approved\nproperties:\n  - name: Status\n    type: String\n    expression: if(ApprovedEventActive, =done, =pending)");
        var context = projected
            ? model.CreateContext("Project", new() { ["Events"] = new BsonDocument() })
            : model.CreateContext(new WorkflowInstance
                { WorkflowDefinition = "Project", Properties = [], Events = [] });
        Assert.Equal("pending", context.Get("Status"));
    }

    [Theory]
    [InlineData("onSubmit")]
    [InlineData("onSave")]
    public void Parser_ComputedPropertyCanUseEventDeclaredByFormEffect(string effects)
    {
        var model = Parse("""
                          name: Project
                          properties:
                            - name: Status
                              type: String
                              expression: if(ApprovedEventActive, =done, =pending)
                          """, new()
        {
            ["Project/Forms/Edit.yaml"] = $"name: Edit\n{effects}:\n  - event: Approved"
        });
        var instance = new WorkflowInstance { WorkflowDefinition = "Project", Properties = [], Events = [] };

        Assert.Equal("pending", model.CreateContext(instance).Get("Status"));
        instance.Events["Approved"] = new InstanceEvent { Id = "Approved", Date = DateTime.UtcNow };
        Assert.Equal("done", model.CreateContext(instance).Get("Status"));
    }

    [Fact]
    public void Context_DoesNotComputeFromUnprojectedLastEvent()
    {
        var model = Parse("""
                          name: Project
                          properties:
                            - name: Name
                              type: String
                            - name: LastChanged
                              type: String!
                              expression: dateLong(LastEvent)
                          """);

        var context = model.CreateContext("Project", new() { ["Name"] = "Example" }, ["Name"]);

        Assert.Equal("Example", context.Get("Name"));
        Assert.False(context.Values.ContainsKey("LastEvent"));
        Assert.False(context.Values.ContainsKey("LastChanged"));
    }

    [Theory]
    [InlineData("=000000000000000000000001")]
    [InlineData("TargetId")]
    [InlineData("if(true, Source, Source)")]
    public void Parser_RejectsComputedReferenceThatIsNotADirectAlias(string expression)
    {
        var exception = Assert.ThrowsAny<Exception>(() => Parse($"""
                                                                 name: Project
                                                                 properties:
                                                                   - name: Source
                                                                     type: Target
                                                                   - name: TargetId
                                                                     type: String
                                                                   - name: Primary
                                                                     type: Target
                                                                     expression: {expression}
                                                                 """, new()
        {
            ["Target/Entity.yaml"] = "name: Target\nproperties:\n  - name: Name\n    type: String"
        }));

        Assert.Contains("Primary", exception.Message);
        Assert.Contains("alias", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, "Target.Name")]
    [InlineData(false, "Target.Parent.Name")]
    [InlineData(false, "Target.Display")]
    public void Parser_RejectsComputedInputsRequiringUnsupportedReferenceLoading(bool embedded, string expression)
    {
        var exception = Assert.ThrowsAny<Exception>(() => Parse($"""
                                                                 name: Project
                                                                 isEmbedded: {embedded.ToString().ToLowerInvariant()}
                                                                 properties:
                                                                   - name: Target
                                                                     type: Target
                                                                   - name: Display
                                                                     type: String
                                                                     expression: {expression}
                                                                 """, new()
        {
            ["Target/Entity.yaml"] = """
                                     name: Target
                                     properties:
                                       - name: Name
                                         type: String
                                       - name: Parent
                                         type: Target
                                       - name: Display
                                         type: String
                                         expression: Parent.Name
                                     """
        }));

        Assert.Contains("Display", exception.Message);
        Assert.Contains("reference", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PermissionRules_CanUseComputedValuesFromLocalSources(bool blocked)
    {
        var model = Parse("""
                          name: Project
                          properties:
                            - name: Blocked
                              type: Check
                            - name: IsBlocked
                              type: Check
                              expression: Blocked
                          """, new()
        {
            ["Project/Roles/Registered.yaml"] = """
                                                name: Registered
                                                actions:
                                                  - type: Edit
                                                    condition:
                                                      not: true
                                                      value:
                                                        property: IsBlocked
                                                        equal: true
                                                """
        });
        var users = new Mock<IUserService>();
        users.Setup(user => user.GetRolesOfCurrentUser(default)).ReturnsAsync([]);
        var rights = new RightsService(model, users.Object, Mock.Of<IWorkflowInstanceRepository>());
        var instance = new WorkflowInstance
        {
            WorkflowDefinition = "Project", Properties = new() { ["Blocked"] = blocked }, Events = []
        };

        Assert.Equal(!blocked, await rights.Can(instance, RoleAction.Edit));
    }

    [Fact]
    public void Parser_RejectsAssessmentBackedComputedPermissionRule()
    {
        var exception = Assert.ThrowsAny<Exception>(() => Parse("""
                                                                name: Project
                                                                properties:
                                                                  - name: Grade
                                                                    type: Double
                                                                    expression: Assessment.FinalGrade
                                                                """, new()
        {
            ["Project/Roles/Registered.yaml"] = """
                                                name: Registered
                                                actions:
                                                  - type: Edit
                                                    condition:
                                                      not: true
                                                      value:
                                                        property: Grade
                                                        equal: 10
                                                """
        }));

        Assert.Contains("permission", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Assessment", exception.Message);
    }

    internal static ModelService Parse(string yaml = Properties, Dictionary<string, string>? extra = null)
    {
        var files = new Dictionary<string, string>
        {
            ["Project/Entity.yaml"] = yaml,
            ["Party/Entity.yaml"] =
                "name: Party\nisEmbedded: true\nproperties:\n  - name: Country\n    type: '[String]'"
        };
        foreach (var pair in extra ?? []) files[pair.Key] = pair.Value;
        return new ModelService(new ModelParser(new DictionaryProvider(files)));
    }

    internal static WorkflowInstance Instance()
    {
        return new WorkflowInstance
        {
            Id = ObjectId.GenerateNewId().ToString(),
            WorkflowDefinition = "Project",
            Properties = new Dictionary<string, BsonValue>
            {
                ["Parties"] = new BsonArray
                {
                    new BsonDocument("Country", new BsonArray { "NL", "BE" }),
                    new BsonDocument("Country", new BsonArray { "nl" })
                }
            },
            Events = []
        };
    }
}