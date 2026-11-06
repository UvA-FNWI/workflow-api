using UvA.Workflow.Tools;

namespace UvA.Workflow.Tests;

public class LinkedStepConfigurationTests
{
    internal static Dictionary<string, string> Files() => new()
    {
        ["Shared/Entity.yaml"] =
            "name: Shared\nsteps: [Upload, Assess]\nproperties:\n  - name: Student\n    type: '[User]'",
        ["Shared/Steps/Upload.yaml"] = "name: Upload\ntitle: Joint upload\nends:\n  event: Uploaded",
        ["Shared/Steps/Assess.yaml"] = "name: Assess\ntitle: Joint assessment\nends:\n  event: Assessed",
        ["Individual/Entity.yaml"] =
            "name: Individual\nsteps: [Before, SharedUpload, SharedAssessment, After]\nproperties:\n  - name: SharedThesis\n    type: Shared!\n  - name: Student\n    type: User",
        ["Individual/Steps/Before.yaml"] = "name: Before\nends:\n  event: Started",
        ["Individual/Steps/SharedUpload.yaml"] =
            "name: SharedUpload\nimport:\n  reference: SharedThesis\n  step: Upload",
        ["Individual/Steps/SharedAssessment.yaml"] =
            "name: SharedAssessment\nimport:\n  reference: SharedThesis\n  step: Assess",
        ["Individual/Steps/After.yaml"] = "name: After\nends:\n  event: Finished"
    };

    [Fact]
    public void Import_CanReferToAnOrdinaryStepInAnotherDefinition()
    {
        var model = new ModelParser(new DictionaryProvider(Files()));
        Assert.Equal(4, model.WorkflowDefinitions["Individual"].Steps.Count);
        Assert.Equal(2, model.WorkflowDefinitions["Shared"].Steps.Count);
    }

    [Theory]
    [InlineData("[Missing]")]
    [InlineData("[Title]")]
    [InlineData("[Student, Title]")]
    [InlineData("[null]")]
    [InlineData("null")]
    public void Import_RejectsParticipantsThatAreNotSourceUserProperties(string participants)
    {
        var files = Files();
        files["Shared/Entity.yaml"] += "\n  - name: Title\n    type: String";
        files["Individual/Steps/SharedUpload.yaml"] += "\n  participants: " + participants;

        Assert.Contains("participants must name User properties",
            Assert.ThrowsAny<Exception>(() => new ModelParser(new DictionaryProvider(files))).Message);
    }

    [Theory]
    [InlineData("reference: Missing\n  step: Upload")]
    [InlineData("reference: Student\n  step: Upload")]
    [InlineData("reference: SharedThesis\n  step: Missing")]
    public void Import_RejectsAnInvalidReferenceOrStep(string import)
    {
        var files = Files();
        files["Individual/Steps/SharedUpload.yaml"] = "name: SharedUpload\nimport:\n  " + import;
        Assert.ThrowsAny<Exception>(() => new ModelParser(new DictionaryProvider(files)));
    }

    [Theory]
    [InlineData("mode: Alongside")]
    [InlineData("ends:\n  event: LocalCompleted")]
    [InlineData("title: Local title")]
    [InlineData("children: [After]")]
    public void Import_RejectsLocalBehavior(string behavior)
    {
        var files = Files();
        files["Individual/Steps/SharedUpload.yaml"] += "\n" + behavior;
        Assert.ThrowsAny<Exception>(() => new ModelParser(new DictionaryProvider(files)));
    }

    [Fact]
    public void Import_RejectsArrayReferencesAndGroupedSources()
    {
        var files = Files();
        files["Individual/Entity.yaml"] = files["Individual/Entity.yaml"].Replace("Shared!", "'[Shared]!'");
        Assert.Contains("single-valued reference",
            Assert.ThrowsAny<Exception>(() => new ModelParser(new DictionaryProvider(files))).Message);
        files = Files();
        files["Shared/Steps/Upload.yaml"] += "\nchildren: [Assess]";
        Assert.ThrowsAny<Exception>(() => new ModelParser(new DictionaryProvider(files)));
    }

    [Theory]
    [InlineData("icon:\n  type: users-line")]
    [InlineData("resultsType: AssessmentFinalOverview")]
    [InlineData("childrenLayout: CollapsibleRows")]
    public void Import_CannotAcquireLocalBehaviorThroughInheritance(string behavior)
    {
        var files = Files();
        files["Base/Entity.yaml"] = "name: Base\nsteps: [SharedUpload]";
        files["Base/Steps/SharedUpload.yaml"] = "name: SharedUpload\n" + behavior;
        files["Individual/Entity.yaml"] += "\ninheritsFrom: Base";

        Assert.ThrowsAny<Exception>(() => new ModelParser(new DictionaryProvider(files)));
    }
}