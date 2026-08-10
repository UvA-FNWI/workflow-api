using UvA.Workflow.Events;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

/// <summary>
/// Runtime behaviour of resetParentStep, driven through the "Reset" fixture, a
/// two-level thesis flow:
///
///   Thesis (sequential)
///   ├── Proposal                  ends: ProposalSubmitted     first child
///   └── Writing (sequential)      declares RejectProposal  -> resets Thesis (back to Proposal)
///       ├── Draft                 ends: DraftSubmitted        first child
///       └── Review (parallel)     declares RequestRevision -> resets Writing (back to Draft)
///           ├── SupervisorReview  ends: SupervisorApproved
///           └── ExaminerReview    ends: ExaminerApproved
///
/// The fixture has NO hand-written suppressions, so every backward move a test
/// observes is produced solely by the generated reset graph. If expansion broke,
/// nothing would go back and these tests would fail.
///
/// Flattened order (what FindOpenStep walks): Proposal, Draft, Review.
/// </summary>
public class ResetParentStepTests
{
    private readonly ModelService _model;
    private readonly WorkflowDefinition _def;

    // Fixed, strictly increasing UTC timeline. Suppression compares dates with a
    // strict '>', so only the ordering matters.
    private static readonly DateTime Base = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static DateTime T(int step) => Base.AddMinutes(step);

    public ResetParentStepTests()
    {
        _model = new ModelService(UnitTestsHelpers.CreateModelParser());
        _def = _model.WorkflowDefinitions["Reset"];
    }

    private static WorkflowInstance NewInstance() =>
        new WorkflowInstanceBuilder().With(workflowDefinition: "Reset", currentStep: "Proposal").Build();

    // Record (or re-record) an event at a point on the timeline.
    private static void Emit(WorkflowInstance i, string ev, DateTime date)
    {
        if (i.Events.TryGetValue(ev, out var existing))
            existing.Date = date;
        else
            i.Events[ev] = new InstanceEvent { Id = ev, Date = date };
    }

    private string? OpenStep(WorkflowInstance i) => _model.FindOpenStep(i)?.Name;
    private bool Active(WorkflowInstance i, string ev) => EventSuppressionHelper.IsEventActive(ev, i, _def);

    private static WorkflowInstance CompletedInstance()
    {
        var i = NewInstance();
        Emit(i, "ProposalSubmitted", T(1));
        Emit(i, "DraftSubmitted", T(2));
        Emit(i, "SupervisorApproved", T(3));
        Emit(i, "ExaminerApproved", T(4));
        return i;
    }

    [Fact]
    public void Parser_GeneratesSuppressionGraph_ForNestedResets()
    {
        // Reset -> restart edges.
        Assert.Equal(["DraftSubmitted"], Suppresses("RequestRevision"));
        Assert.Equal(["ProposalSubmitted", "RequestRevision"], Suppresses("RejectProposal"));

        // Restart -> (completion events + nested resets) edges, scoped to each target.
        Assert.Equal(["ExaminerApproved", "RequestRevision", "SupervisorApproved"], Suppresses("DraftSubmitted"));
        Assert.Equal(
            ["DraftSubmitted", "ExaminerApproved", "RejectProposal", "RequestRevision", "SupervisorApproved"],
            Suppresses("ProposalSubmitted"));

        // The fixture authors no suppressions; the whole graph is generated.
        Assert.Empty(Suppresses("SupervisorApproved"));
        Assert.Empty(Suppresses("ExaminerApproved"));
    }

    [Fact]
    public void HappyPath_CompletesWhenBothReviewsApproved()
    {
        var i = CompletedInstance();
        Assert.Null(OpenStep(i));
    }

    [Fact]
    public void RequestRevision_ReopensDraft_AndKeepsReviewsVisible()
    {
        var i = CompletedInstance();

        Emit(i, "RequestRevision", T(5));

        // Went back one step: Draft reopens even though the review "ended".
        Assert.Equal("Draft", OpenStep(i));
        Assert.False(Active(i, "DraftSubmitted"));

        // Two-phase: the previous pass's reviews stay visible while the student revises.
        Assert.True(Active(i, "SupervisorApproved"));
        Assert.True(Active(i, "ExaminerApproved"));
        Assert.True(Active(i, "RequestRevision"));
    }

    [Fact]
    public void DraftResubmission_InvalidatesPreviousReviews_AndReopensReview()
    {
        var i = CompletedInstance();
        Emit(i, "RequestRevision", T(5));

        Emit(i, "DraftSubmitted", T(6)); // resubmit the draft -> starts a new pass

        Assert.Equal("Review", OpenStep(i));
        Assert.True(Active(i, "DraftSubmitted"));
        Assert.False(Active(i, "SupervisorApproved")); // stale reviews cleared on resubmission
        Assert.False(Active(i, "ExaminerApproved"));
        Assert.False(Active(i, "RequestRevision")); // reset marker cleared on resubmission

        // A fresh review round completes normally.
        Emit(i, "SupervisorApproved", T(7));
        Emit(i, "ExaminerApproved", T(8));
        Assert.Null(OpenStep(i));
        Assert.True(Active(i, "SupervisorApproved"));
    }

    [Fact]
    public void RejectProposal_GoesBackToProposal_ThenCascadeInvalidatesOnResubmission()
    {
        var i = CompletedInstance();

        Emit(i, "RejectProposal", T(5));

        // Went all the way back to the very first step.
        Assert.Equal("Proposal", OpenStep(i));
        Assert.False(Active(i, "ProposalSubmitted"));

        // Later outcomes remain visible until the proposal is resubmitted.
        Assert.True(Active(i, "DraftSubmitted"));
        Assert.True(Active(i, "SupervisorApproved"));

        Emit(i, "ProposalSubmitted", T(6)); // resubmit the proposal

        // The whole Writing subtree is invalidated; we resume at the Draft.
        Assert.Equal("Draft", OpenStep(i));
        Assert.False(Active(i, "DraftSubmitted"));
        Assert.False(Active(i, "SupervisorApproved"));
        Assert.False(Active(i, "ExaminerApproved"));
    }

    [Fact]
    public void DraftResubmissionWithoutRevisionRequest_StillInvalidatesPreviousReviews()
    {
        var i = CompletedInstance();

        Emit(i, "DraftSubmitted", T(5)); // resubmit draft with no revision request

        Assert.Equal("Review", OpenStep(i));
        Assert.False(Active(i, "SupervisorApproved"));
        Assert.False(Active(i, "ExaminerApproved"));
    }

    [Fact]
    public void RevisionRequest_BeforeAnyReview_LeavesDraftOpen()
    {
        var i = NewInstance();
        Emit(i, "ProposalSubmitted", T(1));
        Emit(i, "DraftSubmitted", T(2));
        Assert.Equal("Review", OpenStep(i)); // sanity: sitting in review

        Emit(i, "RequestRevision", T(3)); // revision requested before any review recorded

        Assert.Equal("Draft", OpenStep(i));
        Assert.True(Active(i, "RequestRevision"));
    }

    [Fact]
    public void RepeatedRevisionCycles_Converge()
    {
        var i = CompletedInstance();

        for (var cycle = 0; cycle < 3; cycle++)
        {
            var t = 5 + cycle * 3;
            Emit(i, "RequestRevision", T(t));
            Assert.Equal("Draft", OpenStep(i));

            Emit(i, "DraftSubmitted", T(t + 1));
            Assert.Equal("Review", OpenStep(i));

            Emit(i, "SupervisorApproved", T(t + 2));
            Emit(i, "ExaminerApproved", T(t + 3));
            Assert.Null(OpenStep(i));
        }
    }

    private string[] Suppresses(string ev) =>
        _def.Events.FirstOrDefault(e => e.Name == ev)?.Suppresses?.OrderBy(x => x, StringComparer.Ordinal).ToArray()
        ?? [];
}