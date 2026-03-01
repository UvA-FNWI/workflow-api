using MongoDB.Bson;
using UvA.Workflow.Entities.Domain.Conditions;
using UvA.Workflow.WorkflowModel.Conditions;

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
        var propertiesV0 =
            RestorePropertiesToVersion(properties, changeVersion, changePath, changeOldValue, targetVersion: 0);
        Assert.Equal(originalValue, propertiesV0["Status"]?.AsString);

        // Act & Assert: Reverting to version 1 should also show original value
        var propertiesV1 =
            RestorePropertiesToVersion(properties, changeVersion, changePath, changeOldValue, targetVersion: 1);
        Assert.Equal(originalValue, propertiesV1["Status"]?.AsString);

        // Act & Assert: At version 2 (current), should show updated value
        var propertiesV2 =
            RestorePropertiesToVersion(properties, changeVersion, changePath, changeOldValue, targetVersion: 2);
        Assert.Equal(currentValue, propertiesV2["Status"]?.AsString);
    }

    [Fact]
    public void JournalVersionMatching_ShouldFindCorrectVersionBasedOnTimestamp()
    {
        // Arrange: Property was changed at some point (version 2)
        var now = DateTime.UtcNow;
        var changeVersion = 2;
        var changeHappenedAt = now.AddMinutes(-5); // 5 minutes ago
        var changes = new[] { (Version: changeVersion, Timestamp: changeHappenedAt) };

        // Act & Assert: First submission happened before the change (10 minutes ago)
        var firstSubmission = now.AddMinutes(-10);
        var versionAtFirstSubmission = GetJournalVersionAt(changes, firstSubmission);
        Assert.Equal(0, versionAtFirstSubmission);

        // Act & Assert: Second submission happened after the change (2 minutes ago)
        var secondSubmission = now.AddMinutes(-2);
        var versionAtSecondSubmission = GetJournalVersionAt(changes, secondSubmission);
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
        var journalVersionV1 = GetJournalVersionAt(
            journalChanges.Select(c => (c.Version, c.Timestamp)),
            firstSubmission);
        var propsV1 = ApplyJournalChanges(currentProperties, journalChanges, journalVersionV1);

        // Act: Build version 2 (after the edit)
        var journalVersionV2 = GetJournalVersionAt(
            journalChanges.Select(c => (c.Version, c.Timestamp)),
            secondSubmission);
        var propsV2 = ApplyJournalChanges(currentProperties, journalChanges, journalVersionV2);

        // Assert
        Assert.Equal(originalValue, propsV1["ProjectTitle"]?.AsString);
        Assert.Equal(updatedValue, propsV2["ProjectTitle"]?.AsString);
    }

    public static IEnumerable<object?[]> GetAllEventIdsCases()
    {
        yield return [null, Array.Empty<string>()];
        yield return
        [
            new Condition { Event = new EventCondition { Id = "SubmitProposal" } },
            new[] { "SubmitProposal" }
        ];
        yield return
        [
            new Condition
            {
                Logical = new Logical
                {
                    Operator = LogicalOperator.Or,
                    Children =
                    [
                        new Condition { Event = new EventCondition { Id = "ApproveSubject" } },
                        new Condition { Event = new EventCondition { Id = "RejectSubject" } }
                    ]
                }
            },
            new[] { "ApproveSubject", "RejectSubject" }
        ];
        yield return
        [
            new Condition
            {
                Logical = new Logical
                {
                    Operator = LogicalOperator.Or,
                    Children =
                    [
                        new Condition { Event = new EventCondition { Id = "SubmitForm" } },
                        new Condition { Date = new Date { Source = "Deadline" } },
                        new Condition { Event = new EventCondition { Id = "Approve" } }
                    ]
                }
            },
            new[] { "SubmitForm", "Approve" }
        ];
    }

    [Theory]
    [MemberData(nameof(GetAllEventIdsCases))]
    public void GetAllEventIds_ExtractsExpectedIds(Condition? condition, string[] expected)
    {
        var eventIds = condition.GetAllEventIds().OrderBy(id => id).ToArray();
        var expectedOrdered = expected.OrderBy(id => id).ToArray();
        Assert.Equal(expectedOrdered, eventIds);
    }

    [Fact]
    public void SequentialVersioning_TwoCycles_AllEventsGroupedByVersion()
    {
        // Arrange: Sequential parent step with two complete cycles
        // Cycle 1: Submit Start -> Reject (version 1)
        // Cycle 2: Re-submit Start -> Approve (version 2)
        var events = new List<(string EventId, DateTime Timestamp)>
        {
            ("Start", DateTime.UtcNow.AddMinutes(-20)),
            ("RejectSubject", DateTime.UtcNow.AddMinutes(-15)), // End of cycle 1
            ("Start", DateTime.UtcNow.AddMinutes(-10)),
            ("ApproveSubject", DateTime.UtcNow.AddMinutes(-5)) // End of cycle 2
        };

        var completionEventIds = new[] { "RejectSubject", "ApproveSubject" };

        // Act: Assign version numbers
        var versioned = new List<(string EventId, int Version)>();
        int currentVersion = 1;

        foreach (var evt in events)
        {
            versioned.Add((evt.EventId, currentVersion));
            if (completionEventIds.Contains(evt.EventId))
                currentVersion++;
        }

        // Assert: 4 events total, 2 per version
        Assert.Equal(4, versioned.Count);
        Assert.Equal(1, versioned[0].Version); // First Start
        Assert.Equal(1, versioned[1].Version); // RejectSubject
        Assert.Equal(2, versioned[2].Version); // Second Start
        Assert.Equal(2, versioned[3].Version); // ApproveSubject
    }

    [Fact]
    public void SequentialVersioning_IncompleteVersion_IsFilteredOut()
    {
        // Arrange: Sequential parent step with incomplete version (only Start, no Reject yet)
        var tempVersions = new List<(int VersionNumber, string EventId, DateTime Timestamp)>
        {
            (1, "Start", DateTime.UtcNow.AddMinutes(-10))
        };

        var completionEventIds = new[] { "RejectSubject", "ApproveSubject" };

        // Act: Filter to only complete versions
        var consolidatedVersions = tempVersions
            .GroupBy(v => v.VersionNumber)
            .Select(g => new
            {
                VersionNumber = g.Key,
                EventIds = g.Select(v => v.EventId).ToList(),
                SubmittedAt = g.Max(v => v.Timestamp)
            })
            .Where(v => v.EventIds.Any(e => completionEventIds.Contains(e)))
            .ToList();

        // Assert: Incomplete version is filtered out
        Assert.Empty(consolidatedVersions);
    }

    [Fact]
    public void ParallelVersioning_TwoCompleteCycles_AllEventsGroupedByVersion()
    {
        // Arrange: Parallel parent with 2 children completing twice
        var childNames = new[] { "Child1", "Child2" };
        var events = new List<(string EventId, DateTime Timestamp, string ChildName)>
        {
            // First cycle
            ("Event1", DateTime.UtcNow.AddMinutes(-40), "Child1"),
            ("Event2", DateTime.UtcNow.AddMinutes(-35), "Child2"),

            // Second cycle
            ("Event1", DateTime.UtcNow.AddMinutes(-20), "Child1"),
            ("Event2", DateTime.UtcNow.AddMinutes(-15), "Child2")
        };

        // Act: Track completion and assign versions
        var completedChildren = new HashSet<string>();
        var versioned = new List<(string EventId, int Version)>();
        int currentVersion = 1;

        foreach (var evt in events.OrderBy(e => e.Timestamp))
        {
            versioned.Add((evt.EventId, currentVersion));
            completedChildren.Add(evt.ChildName);

            if (completedChildren.Count == childNames.Length)
            {
                currentVersion++;
                completedChildren.Clear();
            }
        }

        // Assert: 4 events, 2 in each version
        Assert.Equal(4, versioned.Count);
        Assert.Equal(1, versioned[0].Version); // First Event1
        Assert.Equal(1, versioned[1].Version); // First Event2
        Assert.Equal(2, versioned[2].Version); // Second Event1
        Assert.Equal(2, versioned[3].Version); // Second Event2
    }

    [Fact]
    public void ParallelVersioning_IncompleteVersion_IsFilteredOut()
    {
        // Arrange: Parallel parent with 3 children, but only 2 have completed
        var tempVersions = new List<(int VersionNumber, string EventId, DateTime Timestamp)>
        {
            (1, "Event1", DateTime.UtcNow.AddMinutes(-20)),
            (1, "Event2", DateTime.UtcNow.AddMinutes(-15))
        };

        var childEventMap = new Dictionary<string, string>
        {
            ["Event1"] = "Child1",
            ["Event2"] = "Child2",
            ["Event3"] = "Child3"
        };

        var childNames = new[] { "Child1", "Child2", "Child3" };

        // Act: Filter to only complete versions
        var consolidatedVersions = tempVersions
            .GroupBy(v => v.VersionNumber)
            .Select(g => new
            {
                VersionNumber = g.Key,
                EventIds = g.Select(v => v.EventId).ToList()
            })
            .Where(v =>
            {
                var childrenWithEvents = v.EventIds
                    .Select(e => childEventMap.GetValueOrDefault(e))
                    .Where(name => name != null)
                    .Distinct()
                    .ToHashSet();
                return childrenWithEvents.Count == childNames.Length;
            })
            .ToList();

        // Assert: Incomplete version is filtered out
        Assert.Empty(consolidatedVersions);
    }

    private static Dictionary<string, BsonValue?> CloneProperties(Dictionary<string, BsonValue?> original)
        => original.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value?.DeepClone()
        );

    private static Dictionary<string, BsonValue?> RestorePropertiesToVersion(
        Dictionary<string, BsonValue?> current,
        int changeVersion,
        string changePath,
        BsonValue? oldValue,
        int targetVersion)
    {
        var reverted = CloneProperties(current);
        if (changeVersion > targetVersion)
            reverted[changePath] = oldValue?.DeepClone();
        return reverted;
    }

    private static int GetJournalVersionAt(
        IEnumerable<(int Version, DateTime Timestamp)> changes,
        DateTime submissionTime)
    {
        var versions = changes
            .Where(c => c.Timestamp < submissionTime)
            .Select(c => c.Version)
            .ToList();

        return versions.Count == 0 ? 0 : versions.Max();
    }

    private static Dictionary<string, BsonValue?> ApplyJournalChanges(
        Dictionary<string, BsonValue?> current,
        IEnumerable<(int Version, BsonValue? OldValue, DateTime Timestamp, string Path)> changes,
        int journalVersion)
    {
        var props = CloneProperties(current);
        foreach (var change in changes.Where(c => c.Version > journalVersion))
            props[change.Path] = change.OldValue?.DeepClone();

        return props;
    }
}