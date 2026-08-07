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

    public static IEnumerable<object?[]> HistoricalEventConditionCases()
    {
        yield return
        [
            null,
            new[] { "SubmitProposal" },
            false
        ];
        yield return
        [
            new Condition { Event = new EventCondition { Id = "SubmitProposal" } },
            new[] { "SubmitProposal" },
            true
        ];
        yield return
        [
            new Condition { Event = new EventCondition { Id = "SubmitProposal" } },
            new[] { "ApproveProposal" },
            false
        ];
        yield return
        [
            new Condition
            {
                Not = true,
                Event = new EventCondition { Id = "RejectProposal" }
            },
            new[] { "ApproveProposal" },
            true
        ];
        yield return
        [
            new Condition
            {
                Logical = new Logical
                {
                    Operator = LogicalOperator.And,
                    Children =
                    [
                        new Condition { Event = new EventCondition { Id = "SubmitProposal" } },
                        new Condition { Event = new EventCondition { Id = "ApproveProposal" } }
                    ]
                }
            },
            new[] { "SubmitProposal", "ApproveProposal" },
            true
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
                        new Condition { Event = new EventCondition { Id = "ApproveProposal" } },
                        new Condition { Event = new EventCondition { Id = "RejectProposal" } }
                    ]
                }
            },
            new[] { "RejectProposal" },
            true
        ];
    }

    [Theory]
    [MemberData(nameof(HistoricalEventConditionCases))]
    public void IsMet_WithHistoricalEventIds_EvaluatesOnlyEventConditions(
        Condition? condition,
        string[] eventIds,
        bool expected)
    {
        Assert.Equal(expected, condition.IsMet(eventIds));
    }

    public static IEnumerable<object[]> UnsupportedHistoricalConditionCases()
    {
        yield return
        [
            new Condition { Date = new Date { Source = "Deadline" } },
            nameof(Date)
        ];
        yield return
        [
            new Condition { Deadline = new Deadline { ExpressionText = "Deadline" } },
            nameof(Deadline)
        ];
        yield return
        [
            new Condition { Value = new Value { Property = "Status", Equal = "=Approved" } },
            nameof(Value)
        ];
    }

    [Theory]
    [MemberData(nameof(UnsupportedHistoricalConditionCases))]
    public void IsMet_WithHistoricalEventIds_ThrowsForUnsupportedConditionTypes(
        Condition condition,
        string conditionType)
    {
        var exception = Assert.Throws<NotSupportedException>(() =>
            condition.IsMet(Array.Empty<string>()));

        Assert.Contains(conditionType, exception.Message);
    }

    [Fact]
    public void IsMet_WithHistoricalEventIds_ThrowsForUnsupportedLogicalChildWithoutShortCircuiting()
    {
        var condition = new Condition
        {
            Logical = new Logical
            {
                Operator = LogicalOperator.Or,
                Children =
                [
                    new Condition { Event = new EventCondition { Id = "Submitted" } },
                    new Condition { Date = new Date { Source = "Deadline" } }
                ]
            }
        };

        var exception = Assert.Throws<NotSupportedException>(() =>
            condition.IsMet(new[] { "Submitted" }));

        Assert.Contains(nameof(Date), exception.Message);
    }

    [Fact]
    public void ParentStepVersioning_CurrentCycleWithoutLastChildCompletionIsNotReturned()
    {
        var submittedAt = DateTime.UtcNow.AddMinutes(-10);
        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("SubjectFeedback")
            .WithEvent("Start", submittedAt)
            .Build();
        var versions = GetStepVersions(instance,
        [
            EventLog(instance, "Start", submittedAt)
        ]);

        Assert.Empty(versions);
    }

    [Fact]
    public void ParentStepVersioning_LastChildCompletionCreatesVersion()
    {
        var submittedAt = DateTime.UtcNow.AddMinutes(-10);
        var feedbackAt = DateTime.UtcNow.AddMinutes(-5);
        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Start")
            .WithEvent("Start", submittedAt)
            .WithEvent("RejectSubject", feedbackAt)
            .Build();
        var versions = GetStepVersions(instance,
        [
            EventLog(instance, "Start", submittedAt),
            EventLog(instance, "RejectSubject", feedbackAt)
        ]);

        var version = Assert.Single(versions);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal(["Start", "RejectSubject"], version.EventIds);
        Assert.Equal(feedbackAt, version.SubmittedAt);
    }

    [Fact]
    public void ParentStepVersioning_FeedbackStaysWithSubmissionAndResubmissionStartsNewVersion()
    {
        var firstSubmittedAt = DateTime.UtcNow.AddMinutes(-15);
        var feedbackAt = DateTime.UtcNow.AddMinutes(-10);
        var secondSubmittedAt = DateTime.UtcNow.AddMinutes(-5);
        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("SubjectFeedback")
            .WithEvent("Start", secondSubmittedAt)
            .WithEvent("RejectSubject", feedbackAt)
            .Build();
        var versions = GetStepVersions(instance,
        [
            EventLog(instance, "Start", firstSubmittedAt),
            EventLog(instance, "RejectSubject", feedbackAt),
            EventLog(instance, "Start", secondSubmittedAt)
        ]);

        var version = Assert.Single(versions);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal(["Start", "RejectSubject"], version.EventIds);
        Assert.Equal(feedbackAt, version.SubmittedAt);
    }

    [Fact]
    public void ParentStepVersioning_EachLastChildCompletionFinishesOneVersion()
    {
        var firstSubmittedAt = DateTime.UtcNow.AddMinutes(-20);
        var rejectedAt = DateTime.UtcNow.AddMinutes(-15);
        var secondSubmittedAt = DateTime.UtcNow.AddMinutes(-10);
        var approvedAt = DateTime.UtcNow.AddMinutes(-5);
        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Upload")
            .Build();
        var versions = GetStepVersions(instance,
        [
            EventLog(instance, "Start", firstSubmittedAt),
            EventLog(instance, "RejectSubject", rejectedAt),
            EventLog(instance, "Start", secondSubmittedAt),
            EventLog(instance, "ApproveSubject", approvedAt)
        ]);

        Assert.Collection(versions,
            first =>
            {
                Assert.Equal(1, first.VersionNumber);
                Assert.Equal(["Start", "RejectSubject"], first.EventIds);
                Assert.Equal(rejectedAt, first.SubmittedAt);
            },
            latest =>
            {
                Assert.Equal(2, latest.VersionNumber);
                Assert.Equal(["Start", "ApproveSubject"], latest.EventIds);
                Assert.Equal(approvedAt, latest.SubmittedAt);
            });
    }

    [Fact]
    public void ParentStepVersioning_CompletedStepIncludesLatestVersion()
    {
        var firstSubmittedAt = DateTime.UtcNow.AddMinutes(-20);
        var rejectedAt = DateTime.UtcNow.AddMinutes(-15);
        var secondSubmittedAt = DateTime.UtcNow.AddMinutes(-10);
        var approvedAt = DateTime.UtcNow.AddMinutes(-5);
        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Upload")
            .WithEvent("RejectSubject", rejectedAt)
            .WithEvent("Start", secondSubmittedAt)
            .WithEvent("ApproveSubject", approvedAt)
            .Build();
        var versions = GetStepVersions(instance,
        [
            EventLog(instance, "Start", firstSubmittedAt),
            EventLog(instance, "RejectSubject", rejectedAt),
            EventLog(instance, "Start", secondSubmittedAt),
            EventLog(instance, "ApproveSubject", approvedAt)
        ]);

        Assert.Collection(versions,
            first =>
            {
                Assert.Equal(1, first.VersionNumber);
                Assert.Equal(["Start", "RejectSubject"], first.EventIds);
                Assert.Equal(rejectedAt, first.SubmittedAt);
            },
            latest =>
            {
                Assert.Equal(2, latest.VersionNumber);
                Assert.Equal(["Start", "ApproveSubject"], latest.EventIds);
                Assert.Equal(approvedAt, latest.SubmittedAt);
            });
    }

    [Fact]
    public void ParentStepVersioning_WaitsForCompositeLastChildCompletionCondition()
    {
        var submittedAt = DateTime.UtcNow.AddMinutes(-10);
        var approvedAt = DateTime.UtcNow.AddMinutes(-5);
        var rejectedAt = DateTime.UtcNow.AddMinutes(-2);
        var instance = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Start")
            .Build();

        void ConfigureModel(ModelService modelService)
        {
            modelService.WorkflowDefinitions["Project"].AllSteps
                .Single(step => step.Name == "SubjectFeedback").Ends = new Condition
            {
                Logical = new Logical
                {
                    Operator = LogicalOperator.And,
                    Children =
                    [
                        new Condition { Event = new EventCondition { Id = "RejectSubject" } },
                        new Condition { Event = new EventCondition { Id = "ApproveSubject" } }
                    ]
                }
            };
        }

        var incompleteVersions = GetStepVersions(instance,
        [
            EventLog(instance, "Start", submittedAt),
            EventLog(instance, "RejectSubject", rejectedAt)
        ], ConfigureModel);
        var completeVersions = GetStepVersions(instance,
        [
            EventLog(instance, "Start", submittedAt),
            EventLog(instance, "ApproveSubject", approvedAt),
            EventLog(instance, "RejectSubject", rejectedAt)
        ], ConfigureModel);

        Assert.Empty(incompleteVersions);
        var version = Assert.Single(completeVersions);
        Assert.Equal(["Start", "ApproveSubject", "RejectSubject"], version.EventIds);
        Assert.Equal(rejectedAt, version.SubmittedAt);
    }

    [Fact]
    public void RmssProposalVersioning_ParallelLastChildCompletionCreatesVersion()
    {
        var instance = CreateRmssInstance();
        var submittedAt = DateTime.UtcNow.AddMinutes(-10);
        var rejectedAt = DateTime.UtcNow.AddMinutes(-5);
        var approvedAt = DateTime.UtcNow.AddMinutes(-2);
        var versions = GetStepVersions(instance,
        [
            EventLog(instance, "Start", submittedAt),
            EventLog(instance, "ProposalRejectedSupervisor", rejectedAt),
            EventLog(instance, "ProposalApprovedReviewer", approvedAt)
        ]);

        var version = Assert.Single(versions);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal(approvedAt, version.SubmittedAt);
        Assert.Equal(["Start", "ProposalRejectedSupervisor", "ProposalApprovedReviewer"], version.EventIds);
    }

    [Fact]
    public void RmssProposalVersioning_SingleApprovalDoesNotCompleteVersion()
    {
        var instance = CreateRmssInstance();
        var submittedAt = DateTime.UtcNow.AddMinutes(-10);
        var approvedAt = DateTime.UtcNow.AddMinutes(-5);
        var versions = GetStepVersions(instance,
        [
            EventLog(instance, "Start", submittedAt),
            EventLog(instance, "ProposalApprovedSupervisor", approvedAt)
        ]);

        Assert.Empty(versions);
    }

    [Fact]
    public void RmssProposalVersioning_SingleRejectionDoesNotCompleteVersion()
    {
        var instance = CreateRmssInstance();
        var submittedAt = DateTime.UtcNow.AddMinutes(-10);
        var rejectedAt = DateTime.UtcNow.AddMinutes(-5);
        var versions = GetStepVersions(instance,
        [
            EventLog(instance, "Start", submittedAt),
            EventLog(instance, "ProposalRejectedSupervisor", rejectedAt)
        ]);

        Assert.Empty(versions);
    }

    private static Dictionary<string, BsonValue?> CloneProperties(Dictionary<string, BsonValue?> original)
        => original.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value?.DeepClone()
        );

    private static WorkflowInstance CreateRmssInstance()
        => new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project-RMSS")
            .WithCurrentStep("ProposalPhase")
            .Build();

    private static List<StepVersion> GetStepVersions(
        WorkflowInstance instance,
        List<InstanceEventLogEntry> eventLogs,
        Action<ModelService>? configureModel = null)
    {
        var modelProvider = new FileSystemProvider(UnitTestsHelpers.FixturesPath);
        var modelService = new ModelService(new ModelParser(modelProvider));
        configureModel?.Invoke(modelService);
        var workflowDefinition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var step = workflowDefinition.AllSteps.Single(step => step.Name == "Start");
        var parentStep = workflowDefinition.AllSteps.FirstOrDefault(parent =>
            parent.Children.Any(child => child.Name == step.Name));
        var versionedStep = parentStep is { Children.Length: > 1 } ? parentStep : step;

        return new StepVersionService().GetStepVersions(instance, versionedStep, eventLogs);
    }

    private static InstanceEventLogEntry EventLog(
        WorkflowInstance instance,
        string eventId,
        DateTime timestamp)
        => new()
        {
            WorkflowInstanceId = instance.Id,
            EventId = eventId,
            EventDate = timestamp,
            Operation = EventLogOperation.Create,
            Timestamp = timestamp
        };

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