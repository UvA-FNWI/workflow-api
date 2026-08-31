using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using UvA.Workflow.Events;
using UvA.Workflow.Journaling;
using UvA.Workflow.Submissions;
using UvA.Workflow.Users;

namespace UvA.Workflow.Tests.Submissions;

public class AnswerChangeHistoryTests
{
    private static readonly User Alice = new() { UserName = "alice" };
    private static readonly User Bob = new() { UserName = "bob" };

    [Fact]
    public void For_ChangesAcrossStepVersions_GroupsEachWithItsSubmittedValue()
    {
        var submittedV1 = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var editedV1 = submittedV1.AddMinutes(1);
        var submittedV2 = submittedV1.AddMinutes(2);
        var editedV2 = submittedV1.AddMinutes(3);
        var logs = new[] { Submit(submittedV1), Submit(submittedV2, EventLogOperation.Update) };

        var result = AnswerChangeHistory.For(
            [Change("Credits", 6, Alice, editedV1), Change("Credits", 12, Bob, editedV2)],
            "Credits",
            null,
            ["ReviewSubmitted"],
            logs,
            WorkflowDefinition(),
            15);

        Assert.Collection(result,
            version2 =>
            {
                Assert.Equal(2, version2.VersionNumber);
                Assert.Collection(version2.Changes,
                    current =>
                    {
                        Assert.Equal(15, current.Value);
                        Assert.Equal(editedV2, current.ChangedAt);
                        Assert.Equal("bob", current.ChangedBy);
                    },
                    submitted =>
                    {
                        Assert.Equal(12, submitted.Value);
                        Assert.Equal(submittedV2, submitted.ChangedAt);
                        Assert.Null(submitted.ChangedBy);
                    });
            },
            version1 =>
            {
                Assert.Equal(1, version1.VersionNumber);
                Assert.Collection(version1.Changes,
                    current =>
                    {
                        Assert.Equal(12, current.Value);
                        Assert.Equal(editedV1, current.ChangedAt);
                        Assert.Equal("alice", current.ChangedBy);
                    },
                    submitted =>
                    {
                        Assert.Equal(6, submitted.Value);
                        Assert.Equal(submittedV1, submitted.ChangedAt);
                        Assert.Null(submitted.ChangedBy);
                    });
            });
    }

    [Fact]
    public void For_ChangeBeforeFirstSubmission_ReturnsEmpty()
    {
        var submitted = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

        var result = AnswerChangeHistory.For(
            [Change("Credits", 6, Alice, submitted.AddTicks(-1))],
            "Credits", null, ["ReviewSubmitted"],
            [Submit(submitted)], WorkflowDefinition(), 12);

        Assert.Empty(result);
    }

    [Fact]
    public void For_ChangeAtSubmissionTimestamp_IsTheSubmittedValueNotAnEdit()
    {
        var submitted = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

        var result = AnswerChangeHistory.For(
            [Change("Credits", 6, Alice, submitted)],
            "Credits", null, ["ReviewSubmitted"],
            [Submit(submitted)], WorkflowDefinition(), 12);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData("Credits")]
    [InlineData("Review.Credits")]
    public void For_LegacyAndNestedPaths_MatchesAnswer(string path)
    {
        var submitted = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

        var result = AnswerChangeHistory.For(
            [Change(path, 6, Alice, submitted.AddMinutes(1))],
            "Credits", "Review", ["ReviewSubmitted"],
            [Submit(submitted)], WorkflowDefinition(), 12);

        Assert.Single(result);
    }

    [Fact]
    public void For_UnrelatedPath_ReturnsEmpty()
    {
        var submitted = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

        var result = AnswerChangeHistory.For(
            [Change("Title", "old", Alice, submitted.AddMinutes(1))],
            "Credits", null, ["ReviewSubmitted"],
            [Submit(submitted)], WorkflowDefinition(), 12);

        Assert.Empty(result);
    }

    [Fact]
    public void For_FillAfterSubmit_ShowsAsEditOnThatFormSubmit()
    {
        var formSubmitted = new DateTime(2026, 4, 15, 8, 0, 0, DateTimeKind.Utc);
        var topicFilled = formSubmitted.AddDays(21);
        var cycleEnded = new DateTime(2026, 6, 26, 7, 52, 16, DateTimeKind.Utc);
        var logs = new[]
        {
            EventLog("ReviewSubmitted", formSubmitted, EventLogOperation.Create),
            EventLog("ReviewRejected", cycleEnded, EventLogOperation.Create)
        };

        var result = AnswerChangeHistory.For(
            [Change("Topic", BsonNull.Value, Alice, topicFilled)],
            "Topic", null, ["ReviewSubmitted"], logs,
            WorkflowDefinition(), "This is my topic");

        var version1 = Assert.Single(result);
        Assert.Collection(version1.Changes,
            edit =>
            {
                Assert.Equal("This is my topic", edit.Value?.AsString);
                Assert.Equal(topicFilled, edit.ChangedAt);
            },
            submitted =>
            {
                Assert.True(submitted.Value == null || submitted.Value.IsBsonNull);
                Assert.Equal(formSubmitted, submitted.ChangedAt);
            });
    }

    [Fact]
    public void For_QualifyingEdit_IncludesSubmittedVersionsWithoutEdits()
    {
        var submittedV1 = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var rejectedV1 = submittedV1.AddMinutes(1);
        var editedBeforeV2 = submittedV1.AddMinutes(2);
        var submittedV2 = submittedV1.AddMinutes(3);
        var rejectedV2 = submittedV1.AddMinutes(4);
        var editedBeforeV3 = submittedV1.AddMinutes(5);
        var submittedV3 = submittedV1.AddMinutes(6);
        var editedV3 = submittedV1.AddMinutes(7);
        var logs = new[]
        {
            Submit(submittedV1),
            EventLog("ReviewRejected", rejectedV1, EventLogOperation.Create),
            Submit(submittedV2, EventLogOperation.Update),
            EventLog("ReviewRejected", rejectedV2, EventLogOperation.Update),
            Submit(submittedV3, EventLogOperation.Update)
        };

        var result = AnswerChangeHistory.For(
            [
                Change("Credits", "A", Alice, editedBeforeV2),
                Change("Credits", "B", Alice, editedBeforeV3),
                Change("Credits", "C", Alice, editedV3)
            ],
            "Credits", null, ["ReviewSubmitted"], logs, WorkflowDefinition(), "D");

        Assert.Collection(result,
            version3 =>
            {
                Assert.Equal(3, version3.VersionNumber);
                Assert.Collection(version3.Changes,
                    edit => Assert.Equal("D", edit.Value?.AsString),
                    baseline => Assert.Equal("C", baseline.Value?.AsString));
            },
            version2 =>
            {
                Assert.Equal(2, version2.VersionNumber);
                Assert.Equal("B", Assert.Single(version2.Changes).Value?.AsString);
            },
            version1 =>
            {
                Assert.Equal(1, version1.VersionNumber);
                Assert.Equal("A", Assert.Single(version1.Changes).Value?.AsString);
            });
    }

    private static InstanceEventLogEntry Submit(DateTime submittedAt,
        EventLogOperation operation = EventLogOperation.Create)
        => EventLog("ReviewSubmitted", submittedAt, operation);

    private static PropertyChangeEntry Change(string path, BsonValue oldValue, User user, DateTime timestamp)
        => BsonSerializer.Deserialize<PropertyChangeEntry>(new BsonDocument
        {
            [nameof(PropertyChangeEntry.Timestamp)] = timestamp,
            [nameof(PropertyChangeEntry.Path)] = path,
            [nameof(PropertyChangeEntry.OldValue)] = oldValue,
            [nameof(PropertyChangeEntry.ModifiedBy)] = user.UserName
        });

    private static WorkflowDefinition WorkflowDefinition()
        => new()
        {
            Events =
            [
                new EventDefinition { Name = "ReviewSubmitted", Suppresses = ["ReviewRejected"] },
                new EventDefinition { Name = "ReviewRejected", Suppresses = ["ReviewSubmitted"] }
            ]
        };

    private static InstanceEventLogEntry EventLog(string eventId, DateTime timestamp,
        EventLogOperation operation)
        => new()
        {
            EventId = eventId,
            Timestamp = timestamp,
            EventDate = timestamp,
            Operation = operation
        };
}