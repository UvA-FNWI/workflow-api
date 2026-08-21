using MongoDB.Bson;
using UvA.Workflow.Journaling;
using UvA.Workflow.Submissions;
using UvA.Workflow.Users;

namespace UvA.Workflow.Tests.Submissions;

public class AnswerChangeHistoryTests
{
    private static readonly User Alice = new() { UserName = "alice" };
    private static readonly User Bob = new() { UserName = "bob" };
    private static readonly DateTime Submitted = new(2026, 3, 1, 10, 0, 0);

    [Fact]
    public void For_EmptyJournal_ReturnsEmpty()
    {
        var result = AnswerChangeHistory.For([], "Credits", null, Submitted, 12);

        Assert.Empty(result);
    }

    [Fact]
    public void For_UnsubmittedForm_ReturnsEmpty()
    {
        var change = PropertyChangeEntry.Create("Credits", 6, Alice);

        var result = AnswerChangeHistory.For([change], "Credits", null, dateSubmitted: null, 12);

        Assert.Empty(result);
    }

    [Fact]
    public void For_ChangeBeforeSubmit_ReturnsEmpty()
    {
        var change = PropertyChangeEntry.Create("Credits", 6, Alice);

        var result = AnswerChangeHistory.For([change], "Credits", null, DateTime.Now.AddDays(1), 12);

        Assert.Empty(result);
    }

    [Fact]
    public void For_ChangeAfterSubmit_ReturnsCurrentThenOriginal()
    {
        var change = PropertyChangeEntry.Create("Credits", 6, Alice);

        var result = AnswerChangeHistory.For([change], "Credits", null, Submitted, 12);

        Assert.Equal(2, result.Length);
        Assert.Equal(2, result[0].Version);
        Assert.Equal(12, result[0].Value);
        Assert.Equal(change.Timestamp, result[0].ChangedAt);
        Assert.Equal("alice", result[0].ChangedBy);
        Assert.Equal(1, result[1].Version);
        Assert.Equal(6, result[1].Value);
        Assert.Equal(Submitted, result[1].ChangedAt);
        Assert.Null(result[1].ChangedBy);
    }

    [Fact]
    public void For_TwoEdits_ReconstructsValuesNewestFirst()
    {
        var first = PropertyChangeEntry.Create("Credits", 6, Alice);
        var second = PropertyChangeEntry.Create("Credits", 12, Bob);

        var result = AnswerChangeHistory.For([first, second], "Credits", null, Submitted, 15);

        Assert.Equal(3, result.Length);
        Assert.Equal(3, result[0].Version);
        Assert.Equal(15, result[0].Value);
        Assert.Equal(second.Timestamp, result[0].ChangedAt);
        Assert.Equal("bob", result[0].ChangedBy);
        Assert.Equal(2, result[1].Version);
        Assert.Equal(12, result[1].Value);
        Assert.Equal(first.Timestamp, result[1].ChangedAt);
        Assert.Equal("alice", result[1].ChangedBy);
        Assert.Equal(1, result[2].Version);
        Assert.Equal(6, result[2].Value);
    }

    [Fact]
    public void For_NestedPath_MatchesFormProperty()
    {
        var change = PropertyChangeEntry.Create("Review.Credits", 6, Alice);

        var result = AnswerChangeHistory.For([change], "Credits", "Review", Submitted, 12);

        Assert.Equal(2, result.Length);
        Assert.Equal(12, result[0].Value);
        Assert.Equal(6, result[1].Value);
    }

    [Fact]
    public void For_LegacyBarePath_MatchesQuestionName()
    {
        var change = PropertyChangeEntry.Create("Credits", 6, Alice);

        var result = AnswerChangeHistory.For([change], "Credits", "Review", Submitted, 12);

        Assert.Equal(2, result.Length);
    }

    [Fact]
    public void For_UnrelatedPath_IsIgnored()
    {
        var change = PropertyChangeEntry.Create("Title", "old", Alice);

        var result = AnswerChangeHistory.For([change], "Credits", null, Submitted, 12);

        Assert.Empty(result);
    }
}