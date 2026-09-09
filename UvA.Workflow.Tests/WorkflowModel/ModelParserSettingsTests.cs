using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests.WorkflowModel;

public class ModelParserSettingsTests
{
    [Fact]
    public void LoadsSettingsYaml_WhenPresentInConfigRoot()
    {
        var parser = UnitTestsHelpers.CreateModelParser();

        Assert.Equal("Registers", parser.Settings?.Ui?.Overview?.Title?.En);
        Assert.Equal("No access", parser.Settings?.Ui?.Overview?.NoAccess?.En);
    }
}