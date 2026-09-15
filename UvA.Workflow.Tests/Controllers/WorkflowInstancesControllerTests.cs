using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowInstances;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Events;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public class WorkflowInstancesControllerTests : ControllerTestsBase
{
    private const string WorkflowDefinition = "Project";

    private WorkflowInstancesController CreateController(UndoService? undoService = null)
        // Only the dependencies GetInstances touches are wired; the rest are unused here.
        => new(
            _userServiceMock.Object,
            null!,
            _rightsService,
            null!,
            _workflowInstanceRepoMock.Object,
            _instanceService,
            null!,
            null!,
            _modelService,
            null!,
            null!,
            undoService!);

    private void MockInstances(params Dictionary<string, BsonValue>[] rows)
        => _workflowInstanceRepoMock
            .Setup(r => r.GetAllByType(WorkflowDefinition,
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows.ToList());

    [Fact]
    public async Task Undo_InvalidReason_ReturnsBadRequest()
    {
        var result = await CreateController().Undo("instance", new UndoRequest("operation", "  "), _ct);

        var error = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(400, error.StatusCode);
        Assert.Equal("InvalidUndoReason", Assert.IsType<Error>(error.Value).ErrorCode);
    }

    [Fact]
    public async Task Undo_MissingInstance_ReturnsNotFound()
    {
        MockCurrentUser("Undoer");

        var result = await CreateController().Undo(
            "instance", new UndoRequest("operation", "because"), _ct);

        var error = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(404, error.StatusCode);
    }

    [Fact]
    public async Task Undo_StaleCandidateReturnsConflict()
    {
        var instance = new WorkflowInstanceBuilder().With("Project", "Subject").Build();
        MockCurrentUser("Undoer");
        MockInstance(instance);
        _eventRepoMock.Setup(repository => repository.GetEventLogEntriesForInstance(instance.Id, _ct))
            .ReturnsAsync([]);

        var result = await CreateController(_undoService).Undo(
            instance.Id, new UndoRequest("missing-operation", "because"), _ct);

        var error = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(409, error.StatusCode);
        Assert.Equal("UndoCandidateChanged", Assert.IsType<Error>(error.Value).ErrorCode);
    }

    [Fact]
    public async Task Undo_WithoutPermission_ReturnsForbidden()
    {
        var instance = new WorkflowInstanceBuilder().With("Project", "Subject").Build();
        MockCurrentUser("Student");
        MockInstance(instance);
        _eventRepoMock.Setup(r => r.GetEventLogEntriesForInstance(instance.Id, _ct))
            .ReturnsAsync([
                new InstanceEventLogEntry
                {
                    Id = "operation",
                    WorkflowInstanceId = instance.Id,
                    EventId = "Start",
                    Type = EventLogOperation.Create,
                    OperationId = "operation",
                    OperationMetadata = new OperationMetadata
                    {
                        Id = "operation",
                        Type = OperationType.FormSubmission,
                        Source = "Start",
                        Step = "Subject",
                        TopLevelStep = "Subject"
                    }
                }
            ]);

        var undoService = new UndoService(_eventRepoMock.Object, _jobRepositoryMock.Object,
            _workflowInstanceRepoMock.Object,
            _instanceService, _rightsService);
        var result = await CreateController(undoService).Undo(
            instance.Id, new UndoRequest("operation", "because"), _ct);

        var error = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, error.StatusCode);
    }

    [Fact]
    public async Task GetInstances_IncludeTitle_RendersTitleFromTemplateAndCreatedOn()
    {
        // Arrange — SystemAdmin grants ViewAdminTools, which GetInstances requires.
        MockCurrentUser("SystemAdmin");
        var id = ObjectId.GenerateNewId();
        var createdOn = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        // Project's instanceTitle template is '{{ Title }}', so the Title property feeds the rendered title.
        MockInstances(new Dictionary<string, BsonValue>
        {
            ["_id"] = new BsonObjectId(id),
            ["Title"] = new BsonString("Thesis A"),
            ["CreatedOn"] = new BsonDateTime(createdOn)
        });

        // Act
        var result = await CreateController().GetInstances(WorkflowDefinition, [], _ct, includeTitle: true);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var rows = Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object>>>(ok.Value).ToList();
        var row = Assert.Single(rows);
        Assert.Equal(id.ToString(), row["id"]);
        Assert.Equal("Thesis A", row["title"]);
        Assert.True(row.ContainsKey("createdOn"));
    }

    [Fact]
    public async Task GetInstances_WithoutIncludeTitle_OmitsTitleButKeepsCreatedOn()
    {
        // Arrange
        MockCurrentUser("SystemAdmin");
        MockInstances(new Dictionary<string, BsonValue>
        {
            ["_id"] = new BsonObjectId(ObjectId.GenerateNewId()),
            ["CreatedOn"] = new BsonDateTime(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc))
        });

        // Act
        var result = await CreateController().GetInstances(WorkflowDefinition, [], _ct);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var row = Assert.Single(Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object>>>(ok.Value));
        Assert.False(row.ContainsKey("title"));
        Assert.True(row.ContainsKey("createdOn"));
    }

    [Fact]
    public async Task RecalculateCurrentSteps_UpdatesAllInstancesOfWorkflowDefinition()
    {
        MockCurrentUser("SystemAdmin");
        var instances = new[]
        {
            new WorkflowInstanceBuilder()
                .With(workflowDefinition: WorkflowDefinition, currentStep: "RenamedStep")
                .WithEvents(
                    b => b.WithId("Start").AsCompleted(),
                    b => b.WithId("ApproveSubject").AsCompleted())
                .Build(),
            new WorkflowInstanceBuilder()
                .With(workflowDefinition: WorkflowDefinition, currentStep: "Upload")
                .WithEvents(
                    b => b.WithId("Start").AsCompleted(),
                    b => b.WithId("ApproveSubject").AsCompleted())
                .Build()
        };
        _workflowInstanceRepoMock
            .Setup(r => r.GetAll(i => i.WorkflowDefinition == WorkflowDefinition,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(instances.ToList());

        var result = await CreateController().RecalculateCurrentSteps(WorkflowDefinition, _ct);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<RecalculateCurrentStepsResultDto>(ok.Value);
        Assert.Equal(2, response.Total);
        Assert.Equal(1, response.Updated);
        Assert.Equal(1, response.Unchanged);
        Assert.All(instances, instance => Assert.Equal("Upload", instance.CurrentStep));
        _workflowInstanceRepoMock.Verify(
            r => r.UpdateField(instances[0].Id, i => i.CurrentStep, "Upload", It.IsAny<CancellationToken>()),
            Times.Once);
        _workflowInstanceRepoMock.Verify(
            r => r.UpdateField(instances[1].Id, i => i.CurrentStep, "Upload", It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecalculateCurrentSteps_WithoutAdminToolsPermission_ReturnsForbidden()
    {
        MockCurrentUser();

        var result = await CreateController().RecalculateCurrentSteps(WorkflowDefinition, _ct);

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, forbidden.StatusCode);
        _workflowInstanceRepoMock.Verify(
            r => r.GetAll(It.IsAny<System.Linq.Expressions.Expression<Func<WorkflowInstance, bool>>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecalculateCurrentSteps_UnknownWorkflowDefinition_ReturnsNotFound()
    {
        MockCurrentUser("SystemAdmin");

        var result = await CreateController().RecalculateCurrentSteps("Unknown", _ct);

        var notFound = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(404, notFound.StatusCode);
        _workflowInstanceRepoMock.Verify(
            r => r.GetAll(It.IsAny<System.Linq.Expressions.Expression<Func<WorkflowInstance, bool>>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}