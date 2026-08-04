using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class ConfigFileReaderTests
{
    private static Dictionary<string, string> ReadFixtures()
        => ConfigFileReader.ReadAll(new FileSystemProvider(UnitTestsHelpers.FixturesPath));

    [Fact]
    public void ReadAll_ReturnsEveryYamlFileKeyedByRelativePath()
    {
        var files = ReadFixtures();

        Assert.Contains("Project/Entity.yaml", files.Keys);
        Assert.Contains("Project/Forms/Start.yaml", files.Keys);
        Assert.All(files.Keys, key => Assert.EndsWith(".yaml", key, StringComparison.OrdinalIgnoreCase));
        Assert.All(files.Values, content => Assert.False(string.IsNullOrWhiteSpace(content)));
    }

    [Fact]
    public void ReadAll_OutputReparsesIntoAnEquivalentModel()
    {
        var files = ReadFixtures();
        var original = UnitTestsHelpers.CreateModelParser();

        var reparsed = new ModelParser(new DictionaryProvider(files));

        Assert.Equal(
            original.WorkflowDefinitions.Keys.OrderBy(k => k),
            reparsed.WorkflowDefinitions.Keys.OrderBy(k => k));
    }
}