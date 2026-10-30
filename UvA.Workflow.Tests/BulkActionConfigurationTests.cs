using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Tests.Helpers;

namespace UvA.Workflow.Tests;

public class BulkActionConfigurationTests
{
    [Fact]
    public void ParsesOrderedBulkActionsWithBilingualLabelsAndIcons()
    {
        var model = Load("CoordinatorApproved");
        var action = Assert.Single(model.WorkflowDefinitions["Project"].Screens
            .Single(screen => screen.Name == "Projects").BulkActions);
        Assert.Equal("CoordinatorApproved", action.Action);
        Assert.Equal("Approve proposals", action.Label.En);
        Assert.Equal("Voorstellen goedkeuren", action.Label.Nl);
        Assert.Equal("checkmark-line", action.Icon.Type);
        Assert.Empty(model.WorkflowDefinitions["Context"].Screens.Single().BulkActions);
    }

    [Theory]
    [InlineData("DoesNotExist")]
    [InlineData("SubmitStart")]
    public void RejectsMissingOrFormActions(string actionName)
    {
        var error = Assert.ThrowsAny<Exception>(() => Load(actionName));
        Assert.Contains("bulk action", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsActionsRequiringComposedMail()
    {
        var files = Files("ManualMail");
        files["Project/Actions.yaml"] += """

                                           - name: ManualMail
                                             type: Execute
                                             roles: [Coordinator]
                                             onAction:
                                               - sendMail:
                                                   sendAutomatically: false
                                         """;
        var error = Assert.ThrowsAny<Exception>(() => new ModelService(new ModelParser(new DictionaryProvider(files))));
        Assert.Contains("bulk action", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ModelService Load(string actionName)
        => new(new ModelParser(new DictionaryProvider(Files(actionName))));

    private static Dictionary<string, string> Files(string actionName)
    {
        var files = ConfigFileReader.ReadAll(new FileSystemProvider(UnitTestsHelpers.FixturesPath));
        files["Project/Actions.yaml"] += """

                                           - name: SubmitStart
                                             type: Submit
                                             roles: [Coordinator]
                                             form: Start
                                         """;
        files["Project/Screens/Projects.yaml"] += $$"""

                                                    bulkActions:
                                                      - action: {{actionName}}
                                                        label:
                                                          en: Approve proposals
                                                          nl: Voorstellen goedkeuren
                                                        icon:
                                                          type: checkmark-line
                                                    """;
        return files;
    }
}