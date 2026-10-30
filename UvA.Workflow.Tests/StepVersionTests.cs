using MongoDB.Bson;
using UvA.Workflow.Events;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Versioning;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests;

public class StepVersionTests
{
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
    public void FirstReachStartsAnEmptyAttemptBeforeAnySubmission()
    {
        var model = new ModelService(UnitTestsHelpers.CreateModelParser());
        var instance = new WorkflowInstanceBuilder().WithWorkflowDefinition("Reset").WithCurrentStep("Proposal")
            .Build();
        var versions = new StepVersionService(model).GetStepVersions(instance,
            new WorkflowInstanceHistory(null, []));

        Assert.Equal(1, versions["Thesis"].Current!.VersionNumber);
        Assert.Null(versions["Thesis"].Current!.CompletionTimestamp);
        Assert.Empty(versions["Thesis"].Current!.Submissions);
        Assert.Null(versions["Writing"].Current);
        Assert.False(versions.ContainsKey("Draft"));
    }

    [Fact]
    public void NestedRestartAdvancesWritingButKeepsTheThesisAttempt()
    {
        var (model, instance, history) = ResetHistory("ProposalSubmitted", "DraftSubmitted",
            "SupervisorApproved", "ExaminerApproved", "RequestRevision");
        var versions = new StepVersionService(model).GetStepVersions(instance, history);

        Assert.Equal(1, versions["Thesis"].Current!.VersionNumber);
        Assert.Null(versions["Thesis"].Current!.CompletionTimestamp);
        Assert.Equal(["Proposal"], versions["Thesis"].Current!.Submissions.Select(s => s.FormName));
        var writing = versions["Writing"];
        Assert.Equal(2, writing.Current!.VersionNumber);
        Assert.Empty(writing.Current.Submissions);
        Assert.Null(writing.Current.CompletionTimestamp);
        var previous = Assert.Single(writing.History);
        Assert.Equal(At(4), previous.CompletionTimestamp);
        Assert.Equal(["Draft", "SupervisorReview", "ExaminerReview"], previous.Submissions.Select(s => s.FormName));
    }

    [Fact]
    public void CompletionKeepsTheSameCurrentAttempt()
    {
        var (model, instance, history) = ResetHistory("ProposalSubmitted", "DraftSubmitted",
            "SupervisorApproved", "ExaminerApproved");
        var versions = new StepVersionService(model).GetStepVersions(instance, history);
        Assert.Equal(1, versions["Thesis"].Current!.VersionNumber);
        Assert.Equal(At(4), versions["Thesis"].Current!.CompletionTimestamp);
        Assert.Empty(versions["Thesis"].History);
        Assert.Equal(At(4), versions["Writing"].Current!.CompletionTimestamp);
    }

    [Fact]
    public void RestartKeepsAnUnfinishedAttemptInHistory()
    {
        var (model, instance, history) = ResetHistory("ProposalSubmitted", "DraftSubmitted",
            "SupervisorApproved", "RequestRevision", "DraftSubmitted");
        var versions = new StepVersionService(model).GetStepVersions(instance, history);
        var writing = versions["Writing"];
        Assert.Equal(2, writing.Current!.VersionNumber);
        Assert.Equal(["Draft"], writing.Current.Submissions.Select(s => s.FormName));
        var previous = Assert.Single(writing.History);
        Assert.Null(previous.CompletionTimestamp);
        Assert.Equal(["Draft", "SupervisorReview"], previous.Submissions.Select(s => s.FormName));
        Assert.Equal(["Proposal", "Draft"], versions["Thesis"].Current!.Submissions.Select(s => s.FormName));
    }

    [Fact]
    public void ParentRestartDefersTheChildAttemptUntilItIsReachedAgain()
    {
        var (model, instance, history) = ResetHistory("ProposalSubmitted", "DraftSubmitted",
            "SupervisorApproved", "ExaminerApproved", "RejectProposal");
        var service = new StepVersionService(model);
        var versions = service.GetStepVersions(instance, history);
        Assert.Equal(2, versions["Thesis"].Current!.VersionNumber);
        Assert.Empty(versions["Thesis"].Current!.Submissions);
        Assert.Equal(1, versions["Writing"].Current!.VersionNumber);
        Assert.Empty(versions["Writing"].History);

        history.EventLogs.Add(new InstanceEventLogEntry
        {
            Id = "6", EventId = "ProposalSubmitted",
            EventDate = At(6), Timestamp = At(6), Operation = EventLogOperation.Update
        });
        versions = service.GetStepVersions(instance, history);
        Assert.Equal(2, versions["Writing"].Current!.VersionNumber);
        Assert.Empty(versions["Writing"].Current!.Submissions);
        Assert.Single(versions["Writing"].History);
        Assert.Equal(["Proposal"], versions["Thesis"].Current!.Submissions.Select(s => s.FormName));
    }

    [Theory]
    [InlineData("ExaminerApproved", 1, true)]
    [InlineData("RequestRevision", 1, false)]
    public void UndoReconstructsEffectiveAttemptBoundaries(string undoneEvent, int number, bool isIncomplete)
    {
        var events = undoneEvent == "ExaminerApproved"
            ? new[] { "ProposalSubmitted", "DraftSubmitted", "SupervisorApproved", "ExaminerApproved" }
            : ["ProposalSubmitted", "DraftSubmitted", "SupervisorApproved", "ExaminerApproved", "RequestRevision"];
        var (model, instance, history) = ResetHistory(events);
        history.EventLogs.Add(new InstanceEventLogEntry
        {
            Id = "undo", Timestamp = At(7),
            Operation = EventLogOperation.Undo, OperationId = history.EventLogs.Last().OperationId
        });
        var writing = new StepVersionService(model).GetStepVersions(instance, history)["Writing"];
        Assert.Equal(number, writing.Current!.VersionNumber);
        Assert.Equal(isIncomplete, writing.Current.CompletionTimestamp == null);
        Assert.Empty(writing.History);
    }

    [Fact]
    public void EquivalentManualAndGeneratedSuppressionHaveTheSameAttempts()
    {
        var (model, instance, history) = ResetHistory("ProposalSubmitted", "DraftSubmitted", "SupervisorApproved",
            "RequestRevision", "DraftSubmitted", "SupervisorApproved", "ExaminerApproved", "RejectProposal",
            "ProposalSubmitted", "DraftSubmitted");
        var generated = new StepVersionService(model).GetStepVersions(instance, history);
        foreach (var step in model.WorkflowDefinitions["Reset"].AllSteps)
            step.Events.Clear();
        model.WorkflowDefinitions["Reset"].AllSteps.Single(step => step.Name == "Thesis").Events =
            [new EventDefinition { Name = "RequestRevision" }, new EventDefinition { Name = "RejectProposal" }];
        var manual = new StepVersionService(model).GetStepVersions(instance, history);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(generated),
            System.Text.Json.JsonSerializer.Serialize(manual));
    }

    [Fact]
    public void AChildCannotBeReachedWhileItsParentConditionIsFalse()
    {
        var (model, instance, history) = ResetHistory("ProposalSubmitted");
        model.WorkflowDefinitions["Reset"].AllSteps.Single(s => s.Name == "Writing").Condition =
            new Condition { Event = new EventCondition { Id = "Enabled" } };
        var versions = new StepVersionService(model).GetStepVersions(instance, history);
        Assert.Null(versions["Writing"].Current);
    }

    [Fact]
    public void EachFormMustContributeAgainAfterAnIndependentRestart()
    {
        var (model, instance, history) = ResetHistory("ProposalSubmitted", "DraftSubmitted", "ExtraDraft",
            "SupervisorApproved", "RequestRevision", "DraftSubmitted");
        model.WorkflowDefinitions["Reset"].Forms.Add(new Form
            { Name = "ExtraDraft", Step = "Draft", SubmittedWhenEvents = ["ExtraDraft"] });
        var versions = new StepVersionService(model).GetStepVersions(instance, history);
        Assert.Equal(["Draft", "ExtraDraft", "SupervisorReview"],
            versions["Writing"].History[0].Submissions.Select(s => s.FormName));
        Assert.Equal(["Draft"], versions["Writing"].Current!.Submissions.Select(s => s.FormName));
        Assert.Equal(["Proposal", "Draft"], versions["Thesis"].Current!.Submissions.Select(s => s.FormName));
    }

    [Fact]
    public void EqualAuditTimestampsStillFollowEventHistoryOrder()
    {
        var (model, instance, history) = ResetHistory("ProposalSubmitted", "DraftSubmitted",
            "SupervisorApproved", "ExaminerApproved", "RequestRevision");
        foreach (var log in history.EventLogs)
            log.Timestamp = At(1);
        var versions = new StepVersionService(model).GetStepVersions(instance, history);
        Assert.Equal(2, versions["Writing"].Current!.VersionNumber);
        Assert.Equal(At(4), Assert.Single(versions["Writing"].History).CompletionTimestamp);
        Assert.Equal(3, versions["Writing"].History[0].Submissions.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InactiveSubmissionsDoNotRemainInTheCurrentAttempt(bool delete)
    {
        var (model, instance, history) = ResetHistory("ProposalSubmitted", "DraftSubmitted", "Pause");
        if (delete)
        {
            history.EventLogs[^1].EventId = "DraftSubmitted";
            history.EventLogs[^1].Operation = EventLogOperation.Delete;
        }
        else
            model.WorkflowDefinitions["Reset"].Events.Add(new EventDefinition
                { Name = "Pause", Suppresses = ["DraftSubmitted"] });

        var writing = new StepVersionService(model).GetStepVersions(instance, history)["Writing"];
        Assert.Equal(1, writing.Current!.VersionNumber);
        Assert.Empty(writing.Current.Submissions);
        Assert.Empty(writing.History);
    }

    [Fact]
    public void AThreeEventSuppressionCycleRestartsItsScope()
    {
        var (model, instance, history) = ResetHistory("ProposalSubmitted", "DraftSubmitted", "SupervisorApproved");
        var definition = model.WorkflowDefinitions["Reset"];
        foreach (var ev in definition.Events)
            ev.Suppresses = [];
        definition.Events.Single(item => item.Name == "ProposalSubmitted").Suppresses = ["DraftSubmitted"];
        definition.Events.Single(item => item.Name == "DraftSubmitted").Suppresses = ["SupervisorApproved"];
        definition.Events.Add(new EventDefinition { Name = "SupervisorApproved", Suppresses = ["ProposalSubmitted"] });
        var thesis = new StepVersionService(model).GetStepVersions(instance, history)["Thesis"];
        Assert.Equal(2, thesis.Current!.VersionNumber);
        Assert.Empty(thesis.Current.Submissions);
        Assert.Single(thesis.History);
    }

    [Fact]
    public void DateCompletionUsesTheHistoricalClockAndKeepsTheFirstAttempt()
    {
        var (model, instance, history) = ResetHistory();
        var definition = model.WorkflowDefinitions["Reset"];
        var proposal = definition.AllSteps.Single(item => item.Name == "Proposal");
        proposal.Ends = new Condition { Date = new Date { Source = "Deadline" } };
        definition.Properties.Add(new DateTimePropertyDefinition { Name = "Deadline", Type = "DateTime" });
        instance.Properties["Deadline"] = At(2);
        definition.Steps = [proposal, definition.AllSteps.Single(item => item.Name == "Writing")];
        proposal.ParentStep = null;
        definition.AllSteps.Single(item => item.Name == "Writing").ParentStep = null;
        foreach (var ev in definition.Events)
            ev.Suppresses = [];
        definition.AllSteps = definition.AllSteps.Where(step => step.Name != "Thesis").ToList();
        var versions = new StepVersionService(model).GetStepVersions(instance, history);
        Assert.Equal(1, versions["Proposal"].Current!.VersionNumber);
        Assert.Equal(At(2), versions["Proposal"].Current!.CompletionTimestamp);
        Assert.Equal(1, versions["Writing"].Current!.VersionNumber);
    }

    [Fact]
    public void AlongsideAndParallelOwnersStartWhenProgressionMakesThemAvailable()
    {
        var (model, instance, history) = ResetHistory("ProposalSubmitted");
        var definition = model.WorkflowDefinitions["Reset"];
        var proposal = definition.AllSteps.Single(step => step.Name == "Proposal");
        var draft = definition.AllSteps.Single(step => step.Name == "Draft");
        var review = definition.AllSteps.Single(step => step.Name == "Review");
        definition.Steps = [proposal, draft, review];
        foreach (var step in definition.Steps)
            step.ParentStep = null;
        draft.Mode = StepMode.Alongside;
        definition.AllSteps = [proposal, draft, review, .. review.Children];
        foreach (var ev in definition.Events)
            ev.Suppresses = [];
        var service = new StepVersionService(model);
        var initial = service.GetStepVersions(instance, new WorkflowInstanceHistory(null, []));
        Assert.Equal(1, initial["Proposal"].Current!.VersionNumber);
        Assert.Null(initial["Draft"].Current);
        Assert.Null(initial["Review"].Current);
        var reached = service.GetStepVersions(instance, history);
        Assert.Equal(1, reached["Draft"].Current!.VersionNumber);
        Assert.Equal(1, reached["Review"].Current!.VersionNumber);
    }

    [Fact]
    public void RemovingSuppressionRestoresTheSubmissionWithinTheSameAttempt()
    {
        var (model, instance, history) = ResetHistory("ProposalSubmitted", "DraftSubmitted", "Pause", "Pause");
        model.WorkflowDefinitions["Reset"].Events.Add(new EventDefinition
            { Name = "Pause", Suppresses = ["DraftSubmitted"] });
        history.EventLogs[^1].Operation = EventLogOperation.Delete;
        var writing = new StepVersionService(model).GetStepVersions(instance, history)["Writing"];
        Assert.Equal(1, writing.Current!.VersionNumber);
        Assert.Equal(["Draft"], writing.Current.Submissions.Select(member => member.FormName));
        Assert.Empty(writing.History);
    }

    [Fact]
    public void DeletingOneMarkerFallsBackToTheFormsEarlierEffectiveSubmission()
    {
        var (model, instance, history) =
            ResetHistory("ProposalSubmitted", "DraftSubmitted", "OtherDraft", "OtherDraft");
        model.WorkflowDefinitions["Reset"].Forms.Single(form => form.Name == "Draft").SubmittedWhenEvents =
            ["DraftSubmitted", "OtherDraft"];
        history.EventLogs[^1].Operation = EventLogOperation.Delete;
        var writing = new StepVersionService(model).GetStepVersions(instance, history)["Writing"];
        Assert.Equal(At(2), Assert.Single(writing.Current!.Submissions).DateSubmitted);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void AnswerEditsAtOrAfterRestartPreserveAttemptHistory(int editMinute)
    {
        var (model, instance, history) = ResetHistory("ProposalSubmitted", "DraftSubmitted", "RequestRevision");
        model.WorkflowDefinitions["Reset"].Forms.Single(form => form.Name == "Draft").Pages =
        [
            new Page
            {
                PageElements =
                [
                    new PageElement
                        { QuestionDefinition = new StringPropertyDefinition { Name = "Title", Type = "String" } }
                ]
            }
        ];
        instance.Properties["Title"] = "New draft";
        var change = new BsonDocument
        {
            ["Timestamp"] = At(editMinute), ["Path"] = "Title", ["OldValue"] = "Previous answer",
            ["ModifiedBy"] = "coordinator"
        };
        var journal = new UvA.Workflow.Journaling.InstanceJournalEntry
        {
            PropertyChanges =
            [
                MongoDB.Bson.Serialization.BsonSerializer
                    .Deserialize<UvA.Workflow.Journaling.PropertyChangeEntry>(change)
            ]
        };
        var versions = new StepVersionService(model).GetStepVersions(instance, history with { Journal = journal });

        var writing = versions["Writing"];
        Assert.Equal(2, writing.Current!.VersionNumber);
        Assert.Empty(writing.Current.Submissions);
        var previous = Assert.Single(writing.History);
        Assert.Equal(1, previous.VersionNumber);
        Assert.Equal(At(3), previous.FrozenAt);
        Assert.Equal("Draft", Assert.Single(previous.Submissions).FormName);
    }

    [Fact]
    public void AnEventDerivedDateBoundaryIsReplayedAfterTheEventArrives()
    {
        var (model, instance, history) = ResetHistory("ProposalSubmitted");
        var definition = model.WorkflowDefinitions["Reset"];
        var proposal = definition.AllSteps.Single(step => step.Name == "Proposal");
        var writing = definition.AllSteps.Single(step => step.Name == "Writing");
        definition.Steps = [proposal, writing];
        proposal.ParentStep = null;
        writing.ParentStep = null;
        definition.AllSteps = definition.AllSteps.Where(step => step.Name != "Thesis").ToList();
        foreach (var ev in definition.Events)
            ev.Suppresses = [];
        proposal.Ends = new Condition
        {
            Logical = new Logical
            {
                Operator = LogicalOperator.And, Children =
                [
                    new Condition { Event = new EventCondition { Id = "ProposalSubmitted" } },
                    new Condition
                    {
                        Not = true,
                        Deadline = new DeadlineCondition { ExpressionText = "addDays(ProposalSubmittedEvent, 1)" }
                    }
                ]
            }
        };
        var versions = new StepVersionService(model).GetStepVersions(instance, history);
        Assert.Equal(At(1).AddDays(1), versions["Proposal"].Current!.CompletionTimestamp);
        Assert.Equal(1, versions["Writing"].Current!.VersionNumber);
    }

    private static DateTime At(int minute) => new(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc);

    private static (ModelService Model, WorkflowInstance Instance, WorkflowInstanceHistory History) ResetHistory(
        params string[] events)
    {
        var model = new ModelService(UnitTestsHelpers.CreateModelParser());
        var definition = model.WorkflowDefinitions["Reset"];
        definition.Forms = definition.AllSteps.Where(s => s.EndEvent != null)
            .Select(s => new Form { Name = s.Name, Step = s.Name, SubmittedWhenEvents = [s.EndEvent!] }).ToList();
        var instance = new WorkflowInstanceBuilder().WithWorkflowDefinition("Reset").WithCurrentStep("Proposal")
            .Build();
        instance.CreatedOn = At(0);
        var logs = events.Select((id, index) => new InstanceEventLogEntry
        {
            Id = (index + 1).ToString(), EventId = id, EventDate = At(index + 1), Timestamp = At(index + 1),
            Operation = EventLogOperation.Create, OperationId = "operation-" + index
        }).ToList();
        instance.Events = EventHistory.RebuildEvents(logs);
        return (model, instance, new WorkflowInstanceHistory(null, logs));
    }
}