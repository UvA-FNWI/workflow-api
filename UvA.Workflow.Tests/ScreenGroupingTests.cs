using UvA.Workflow.Api.Screens.Dtos;

namespace UvA.Workflow.Tests;

public class ScreenGroupingConfigurationTests
{
    [Fact]
    public void ParsesScreenGrouping_WhenEveryLeafStepIsMappedDirectly()
    {
        var model = Parse(Screen("""
                                 - name: subject
                                   title: Subject
                                   steps: [Start, Feedback]
                                 - name: upload
                                   title: Upload
                                   steps: [Upload]
                                 """));

        var groups = model.WorkflowDefinitions["Project"].Screens.Single().Grouping!.Groups;
        Assert.Equal(["subject", "upload"], groups.Select(g => g.Name));
    }

    [Fact]
    public void ExposesConfiguredColumnGroups()
    {
        var model = Parse(Screen("""
                                 - name: subject
                                   title: Subject
                                   steps: [Subject]
                                 - name: upload
                                   title: Upload
                                   steps: [Upload]
                                   enabled: false
                                 """, """
                                      - type: CurrentStep
                                      - name: SingleGroup
                                        value: Subject
                                        groups: [subject]
                                      - name: MultipleGroups
                                        value: Subject and Upload
                                        groups: [subject, upload]
                                      - name: AllGroups
                                        value: All
                                        groups: []
                                      """));

        var columns = model.WorkflowDefinitions["Project"].Screens.Single().Columns.Select(ScreenColumnDto.Create);
        Assert.Collection(columns,
            column => Assert.Empty(column.Groups),
            column => Assert.Equal(["subject"], column.Groups),
            column => Assert.Equal(["subject", "upload"], column.Groups),
            column => Assert.Empty(column.Groups));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsUnknownColumnGroup(bool grouped)
    {
        const string columns = """
                               - name: Progress
                                 type: CurrentStep
                                 groups: [missing]
                               """;
        var screen = grouped
            ? Screen("""
                     - name: active
                       title: Active
                       steps: [Subject, Upload]
                     """, columns)
            : $"name: Projects\ncolumns:\n{Indent(columns, "  ")}";

        var error = Assert.Throws<Exception>(() => Parse(screen));

        Assert.Contains("Screen 'Projects' in 'Project' column 'Progress' references unknown group(s): missing",
            error.Message);
    }

    [Fact]
    public void ParsesScreenGrouping_WhenAParentStepCoversAllOfItsChildren()
    {
        // "Subject" has children Start and Feedback; mapping the parent alone should cover both,
        // mirroring how ScreenDataService.BuildGroups walks up through parent steps at runtime.
        var model = Parse(Screen("""
                                 - name: subject
                                   title: Subject
                                   steps: [Subject]
                                 - name: upload
                                   title: Upload
                                   steps: [Upload]
                                 """));

        Assert.NotNull(model.WorkflowDefinitions["Project"].Screens.Single().Grouping);
    }

    [Fact]
    public void RejectsGroupStepThatDoesNotExist()
    {
        var error = Assert.Throws<Exception>(() => Parse(Screen("""
                                                                - name: subject
                                                                  title: Subject
                                                                  steps: [Start, Feedback, DoesNotExist]
                                                                - name: upload
                                                                  title: Upload
                                                                  steps: [Upload]
                                                                """)));

        Assert.Contains("Screen 'Projects' in 'Project' maps unknown step(s): DoesNotExist", error.Message);
    }

    [Fact]
    public void RejectsGroupStepThatIsAnOrphanedStepFile()
    {
        // A step file that exists under Steps/ but is never referenced from the top-level steps list
        // or another step's children is not part of the workflow and must not be treated as valid.
        var files = Files();
        files["Project/Steps/Orphan.yaml"] = "name: Orphan";
        files["Project/Screens/Projects.yaml"] = Screen("""
                                                        - name: subject
                                                          title: Subject
                                                          steps: [Start, Feedback, Orphan]
                                                        - name: upload
                                                          title: Upload
                                                          steps: [Upload]
                                                        """);

        var error = Assert.Throws<Exception>(() => new ModelParser(new DictionaryProvider(files)));

        Assert.Contains("Screen 'Projects' in 'Project' maps unknown step(s): Orphan", error.Message);
    }

    [Fact]
    public void RejectsScreenThatDoesNotMapEveryStepToAGroup()
    {
        var error = Assert.Throws<Exception>(() => Parse(Screen("""
                                                                - name: subject
                                                                  title: Subject
                                                                  steps: [Start]
                                                                - name: upload
                                                                  title: Upload
                                                                  steps: [Upload]
                                                                """)));

        Assert.Contains(
            "Screen 'Projects' in 'Project' does not map step(s) to any group: Feedback",
            error.Message);
    }

    [Fact]
    public void RejectsScreenThatOmitsAWholeGroupOfSteps()
    {
        var error = Assert.Throws<Exception>(() => Parse(Screen("""
                                                                - name: upload
                                                                  title: Upload
                                                                  steps: [Upload]
                                                                """)));

        Assert.Contains(
            "Screen 'Projects' in 'Project' does not map step(s) to any group: Start, Feedback",
            error.Message);
    }

    [Fact]
    public void ScreensWithoutGroupingAreNotValidatedAgainstSteps()
    {
        var files = Files();
        files["Project/Screens/Projects.yaml"] = """
                                                 name: Projects
                                                 columns:
                                                   - type: CurrentStep
                                                 """;

        var model = new ModelParser(new DictionaryProvider(files));

        Assert.Null(model.WorkflowDefinitions["Project"].Screens.Single().Grouping);
    }

    private static ModelParser Parse(string screenYaml)
    {
        var files = Files();
        files["Project/Screens/Projects.yaml"] = screenYaml;
        return new ModelParser(new DictionaryProvider(files));
    }

    private static string Screen(string groups, string columns = "- type: CurrentStep") =>
        $"""
         name: Projects
         columns:
         {Indent(columns, "  ")}
         grouping:
           groups:
         {Indent(groups, "    ")}
         """;

    private static string Indent(string text, string prefix) =>
        string.Join('\n', text.Split('\n').Select(line => prefix + line));

    private static Dictionary<string, string> Files() => new()
    {
        ["Project/Entity.yaml"] = "name: Project\ntitlePlural: Projects\nsteps: [Subject, Upload]",
        ["Project/Steps/Subject.yaml"] = "name: Subject\nchildren: [Start, Feedback]",
        ["Project/Steps/Start.yaml"] = "name: Start",
        ["Project/Steps/Feedback.yaml"] = "name: Feedback",
        ["Project/Steps/Upload.yaml"] = "name: Upload"
    };
}