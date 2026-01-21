using MongoDB.Bson;

namespace UvA.Workflow.Tests;

public class StepVersionTests
{
    [Fact]
    public void RevertingChanges_ShouldRestoreOldValues()
    {
        // Arrange: User submitted "Draft" initially, then changed it to "Final Review"
        var currentValue = "Final Review";
        var originalValue = "Draft";

        var properties = new Dictionary<string, BsonValue?>
        {
            ["Status"] = BsonValue.Create(currentValue)
        };

        var changeVersion = 2;
        var changeOldValue = BsonValue.Create(originalValue);
        var changePath = "Status";

        // Act & Assert: Reverting to version 0 should show original value
        var propertiesV0 = CloneProperties(properties);
        if (changeVersion > 0)
            propertiesV0[changePath] = changeOldValue?.DeepClone();

        Assert.Equal(originalValue, propertiesV0["Status"]?.AsString);

        // Act & Assert: Reverting to version 1 should also show original value
        var propertiesV1 = CloneProperties(properties);
        if (changeVersion > 1)
            propertiesV1[changePath] = changeOldValue?.DeepClone();

        Assert.Equal(originalValue, propertiesV1["Status"]?.AsString);

        // Act & Assert: At version 2 (current), should show updated value
        var propertiesV2 = CloneProperties(properties);
        if (changeVersion > 2)
            propertiesV2[changePath] = changeOldValue?.DeepClone();

        Assert.Equal(currentValue, propertiesV2["Status"]?.AsString);
    }

    [Fact]
    public void JournalVersionMatching_ShouldFindCorrectVersionBasedOnTimestamp()
    {
        // Arrange: Property was changed at some point (version 2)
        var now = DateTime.UtcNow;
        var changeVersion = 2;
        var changeHappenedAt = now.AddMinutes(-5); // 5 minutes ago

        // Act & Assert: First submission happened before the change (10 minutes ago)
        var firstSubmission = now.AddMinutes(-10);
        var changesBeforeFirst = changeHappenedAt < firstSubmission ? new[] { changeVersion } : Array.Empty<int>();
        var versionAtFirstSubmission = changesBeforeFirst.Any() ? changesBeforeFirst.Max() : 0;

        Assert.Equal(0, versionAtFirstSubmission);

        // Act & Assert: Second submission happened after the change (2 minutes ago)
        var secondSubmission = now.AddMinutes(-2);
        var changesBeforeSecond = changeHappenedAt < secondSubmission ? new[] { changeVersion } : Array.Empty<int>();
        var versionAtSecondSubmission = changesBeforeSecond.Any() ? changesBeforeSecond.Max() : 0;

        Assert.Equal(2, versionAtSecondSubmission);
    }

    [Fact]
    public void FullScenario_TwoSubmissions_ShowsCorrectValuesPerVersion()
    {
        // Arrange: User submitted "Research Proposal" initially, later edited to "Final Proposal"
        var now = DateTime.UtcNow;
        var originalValue = "Research Proposal";
        var updatedValue = "Final Proposal";

        var currentProperties = new Dictionary<string, BsonValue?>
        {
            ["ProjectTitle"] = BsonValue.Create(updatedValue)
        };

        // Journal shows the edit happened between the two submissions (version 2)
        var journalChanges = new[]
        {
            (Version: 2,
                OldValue: BsonValue.Create(originalValue),
                Timestamp: now.AddMinutes(-5),
                Path: "ProjectTitle")
        };

        // Two submissions: one before edit, one after
        var firstSubmission = now.AddMinutes(-10);
        var secondSubmission = now.AddMinutes(-2);

        // Act: Build version 1 (before the edit)
        var changesBeforeV1 = journalChanges.Where(c => c.Timestamp < firstSubmission).ToList();
        var journalVersionV1 = changesBeforeV1.Any() ? changesBeforeV1.Max(c => c.Version) : 0;

        var propsV1 = CloneProperties(currentProperties);
        foreach (var change in journalChanges.Where(c => c.Version > journalVersionV1))
            propsV1[change.Path] = change.OldValue?.DeepClone();

        // Act: Build version 2 (after the edit)
        var changesBeforeV2 = journalChanges.Where(c => c.Timestamp < secondSubmission).ToList();
        var journalVersionV2 = changesBeforeV2.Any() ? changesBeforeV2.Max(c => c.Version) : 0;

        var propsV2 = CloneProperties(currentProperties);
        foreach (var change in journalChanges.Where(c => c.Version > journalVersionV2))
            propsV2[change.Path] = change.OldValue?.DeepClone();

        // Assert
        Assert.Equal(originalValue, propsV1["ProjectTitle"]?.AsString);
        Assert.Equal(updatedValue, propsV2["ProjectTitle"]?.AsString);
    }

    private static Dictionary<string, BsonValue?> CloneProperties(Dictionary<string, BsonValue?> original)
    {
        return original.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value?.DeepClone()
        );
    }
}