using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Entities.Domain.Conditions;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests;

/// <summary>
/// Integration tests to verify that GetAllEventIds works with real workflow YAML files
/// </summary>
public class ConditionEventExtractionTests
{
    [Fact]
    public void GetAllEventIds_SubjectFeedbackStep_ExtractsBothEvents()
    {
        // Arrange: Replicate the SubjectFeedback.yaml structure
        // ends: 
        //   logical:
        //     operator: Or
        //     children: 
        //     - event: RejectSubject
        //     - event: ApproveSubject

        var step = new Step
        {
            Name = "SubjectFeedback",
            Ends = new Condition
            {
                Logical = new Logical
                {
                    Operator = LogicalOperator.Or,
                    Children = new[]
                    {
                        new Condition { Event = new EventCondition { Id = "RejectSubject" } },
                        new Condition { Event = new EventCondition { Id = "ApproveSubject" } }
                    }
                }
            }
        };

        // Act: Extract all event IDs from the ends condition
        var eventIds = step.Ends.GetAllEventIds().ToList();

        // Assert: Both events should be extracted
        Assert.Equal(2, eventIds.Count);
        Assert.Contains("RejectSubject", eventIds);
        Assert.Contains("ApproveSubject", eventIds);
    }

    [Fact]
    public void GetAllEventIds_SimpleEventStep_ExtractsSingleEvent()
    {
        // Arrange: Like Proposal.yaml with simple event
        var step = new Step
        {
            Name = "Proposal",
            Ends = new Condition
            {
                Event = new EventCondition { Id = "Proposal" }
            }
        };

        // Act
        var eventIds = step.Ends.GetAllEventIds().ToList();

        // Assert
        Assert.Single(eventIds);
        Assert.Equal("Proposal", eventIds[0]);
    }

    [Fact]
    public void GetAllEventIds_DateEndCondition_ReturnsEmpty()
    {
        // Arrange: Like Pending.yaml with date condition
        var step = new Step
        {
            Name = "Pending",
            Ends = new Condition
            {
                Date = new Date { Source = "Round.Deadline" }
            }
        };

        // Act
        var eventIds = step.Ends.GetAllEventIds().ToList();

        // Assert: Date conditions have no events
        Assert.Empty(eventIds);
    }

    [Fact]
    public void GetAllEventIds_StepWithChildren_ExtractsParentAndChildEvents()
    {
        // Arrange: Parent step with its own end event and child steps
        var parentStep = new Step
        {
            Name = "Assessment",
            Ends = new Condition
            {
                Event = new EventCondition { Id = "CompleteAssessment" }
            },
            Children = new[]
            {
                new Step
                {
                    Name = "AssessmentSupervisor",
                    Ends = new Condition
                    {
                        Event = new EventCondition { Id = "AssessmentSupervisor" }
                    }
                },
                new Step
                {
                    Name = "AssessmentReviewer",
                    Ends = new Condition
                    {
                        Event = new EventCondition { Id = "AssessmentReviewer" }
                    }
                }
            }
        };

        // Act: Collect events from parent and all children (mimicking StepVersionService logic)
        var allEventIds = parentStep.Ends.GetAllEventIds()
            .Concat(parentStep.Children.SelectMany(c => c.Ends.GetAllEventIds()))
            .Distinct()
            .ToList();

        // Assert: All three events should be collected
        Assert.Equal(3, allEventIds.Count);
        Assert.Contains("CompleteAssessment", allEventIds);
        Assert.Contains("AssessmentSupervisor", allEventIds);
        Assert.Contains("AssessmentReviewer", allEventIds);
    }

    [Fact]
    public void GetAllEventIds_ChildWithLogicalCondition_ExtractsAllChildEvents()
    {
        // Arrange: Parent with child that has logical OR condition
        var parentStep = new Step
        {
            Name = "ParentStep",
            Children = new[]
            {
                new Step
                {
                    Name = "ChildWithLogicalEnd",
                    Ends = new Condition
                    {
                        Logical = new Logical
                        {
                            Operator = LogicalOperator.Or,
                            Children = new[]
                            {
                                new Condition { Event = new EventCondition { Id = "ChildEvent1" } },
                                new Condition { Event = new EventCondition { Id = "ChildEvent2" } }
                            }
                        }
                    }
                }
            }
        };

        // Act
        var allEventIds = parentStep.Ends.GetAllEventIds()
            .Concat(parentStep.Children.SelectMany(c => c.Ends.GetAllEventIds()))
            .Distinct()
            .ToList();

        // Assert: Both child events should be extracted
        Assert.Equal(2, allEventIds.Count);
        Assert.Contains("ChildEvent1", allEventIds);
        Assert.Contains("ChildEvent2", allEventIds);
    }
}