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
    [InlineData("DoesNotExist", "action does not exist")]
    [InlineData("SubmitStart", "only Execute actions are supported")]
    public void RejectsMissingOrFormActions(string actionName, string reason)
    {
        var error = Assert.ThrowsAny<Exception>(() => Load(actionName));
        AssertDiagnostic(error, actionName, reason);
    }

    [Theory]
    [InlineData("sendMail")]
    [InlineData("sendAccessMail")]
    public void RejectsActionsRequiringComposedMail(string effect)
    {
        var files = Files("ManualMail");
        files["Project/Actions.yaml"] += $$"""

                                             - name: ManualMail
                                               type: Execute
                                               roles: [Coordinator]
                                               onAction:
                                                 - {{effect}}:
                                                     sendAutomatically: false
                                           """;
        var error = Assert.ThrowsAny<Exception>(() => new ModelService(new ModelParser(new DictionaryProvider(files))));
        AssertDiagnostic(error, "ManualMail", "sendAutomatically: true");
    }

    [Theory]
    [InlineData("action: CoordinatorApproved", "action: ''", "action is required")]
    [InlineData("label:\n      en: Approve proposals\n      nl: Voorstellen goedkeuren", "", "label is required")]
    [InlineData("icon:\n      type: checkmark-line", "", "icon.type is required")]
    [InlineData("type: checkmark-line", "type: ''", "icon.type is required")]
    public void ReportsMissingButtonConfiguration(string original, string replacement, string reason)
    {
        var files = Files("CoordinatorApproved");
        files["Project/Screens/Projects.yaml"] = files["Project/Screens/Projects.yaml"].Replace(original, replacement);

        var error = Assert.ThrowsAny<Exception>(() => new ModelService(new ModelParser(new DictionaryProvider(files))));

        AssertDiagnostic(error, original.StartsWith("action:") ? "" : "CoordinatorApproved", reason);
    }

    [Fact]
    public void ReportsDuplicateButtonAction()
    {
        var files = Files("CoordinatorApproved");
        files["Project/Screens/Projects.yaml"] += """

                                                    - action: CoordinatorApproved
                                                      label: Approve again
                                                      icon:
                                                        type: checkmark-line
                                                  """;

        var error = Assert.ThrowsAny<Exception>(() => new ModelService(new ModelParser(new DictionaryProvider(files))));

        AssertDiagnostic(error, "CoordinatorApproved", "action is configured more than once");
    }

    private static void AssertDiagnostic(Exception error, string actionName, string reason)
    {
        Assert.Contains($"Invalid bulk action '{actionName}' on screen 'Projects' in 'Project'", error.Message);
        Assert.Contains(reason, error.Message);
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