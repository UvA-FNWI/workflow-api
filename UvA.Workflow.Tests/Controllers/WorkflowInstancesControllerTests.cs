using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowInstances;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Events;
using UvA.Workflow.Notifications;
using UvA.Workflow.Persistence;
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
            undoService!,
            _mailLogRepositoryMock.Object);

    private void MockInstances(params Dictionary<string, BsonValue>[] rows)
        => _workflowInstanceRepoMock
            .Setup(r => r.GetAllByType(WorkflowDefinition,
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows.ToList());

    private void MockMailLog(string instanceId, params MailLogEntry[] entries)
        => _mailLogRepositoryMock
            .Setup(r => r.GetByInstance(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries.ToList());

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
                    Operation = EventLogOperation.Create,
                    OperationId = "operation",
                    OperationMetadata = new OperationMetadata
                    {
                        Id = "operation",
                        Type = OperationType.FormSubmission,
                        Source = "Start",
                        Step = "Subject"
                    }
                }
            ]);

        var undoService = new UndoService(_eventRepoMock.Object, _jobRepositoryMock.Object,
            _workflowInstanceRepoMock.Object,
            _instanceService, _rightsService, _modelService);
        var result = await CreateController(undoService).Undo(
            instance.Id, new UndoRequest("operation", "because"), _ct);

        var error = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, error.StatusCode);
    }

    [Fact]
    public async Task GetInstances_ComputedPropertyProjectsInputsWithoutExposingThem()
    {
        MockCurrentUser("SystemAdmin");
        var definition = _modelService.WorkflowDefinitions[WorkflowDefinition];
        var computedDefinition = DerivedPropertyTests.Parse().WorkflowDefinitions[WorkflowDefinition];
        definition.Properties.AddRange(computedDefinition.Properties);
        definition.ComputedProperties.AddRange(computedDefinition.ComputedProperties);
        _workflowInstanceRepoMock.Setup(repository => repository.GetAllByType(WorkflowDefinition,
                It.Is<Dictionary<string, string>>(projection => projection.ContainsKey("Parties") &&
                                                                !projection.ContainsKey("Count")), _ct))
            .ReturnsAsync([
                new Dictionary<string, BsonValue>(DerivedPropertyTests.Instance().Properties)
                    { ["_id"] = ObjectId.GenerateNewId() }
            ]);

        var result = await CreateController().GetInstances(WorkflowDefinition, ["Count"], _ct);

        var rows = Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object>>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        var row = Assert.Single(rows);
        Assert.Equal(2, row["count"]);
        Assert.False(row.ContainsKey("parties"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GetInstances_ReturnsRequestedNormalizedEventValues(bool occurred, bool suppressed)
    {
        MockCurrentUser("SystemAdmin");
        var date = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        var events = occurred
            ? new BsonDocument("ApproveSubject", new BsonDocument("Date", date))
            : new BsonDocument();
        if (suppressed)
        {
            _modelService.WorkflowDefinitions[WorkflowDefinition].Events.Add(new EventDefinition
                { Name = "Reset", Suppresses = ["ApproveSubject"] });
            events["Reset"] = new BsonDocument("Date", date.AddDays(1));
        }
        MockInstances(new Dictionary<string, BsonValue>
        {
            ["_id"] = ObjectId.GenerateNewId(),
            ["CreatedOn"] = date.AddDays(-1),
            ["CreateDate"] = date.AddDays(-1),
            ["Events"] = events
        });

        var result = await CreateController().GetInstances(WorkflowDefinition,
            ["ApproveSubjectEvent", "ApproveSubjectEventActive", "LastEvent"], _ct);

        var rows = Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object>>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        var row = Assert.Single(rows);
        Assert.Equal(occurred && !suppressed ? date.ToLocalTime() : (DateTime?)null, row["approveSubjectEvent"]);
        Assert.Equal(occurred && !suppressed, row["approveSubjectEventActive"]);
        Assert.Equal((suppressed ? date.AddDays(1) : occurred ? date : date.AddDays(-1)).ToLocalTime(), row["lastEvent"]);
        Assert.False(row.ContainsKey("events"));
    }

    [Fact]
    public async Task GetInstances_EnrichesReferenceDependentComputedProperty()
    {
        MockCurrentUser("SystemAdmin");
        MockEmptyRelatedInstanceLookups();
        var computed = DerivedPropertyTests.Parse("name: Project\nproperties:\n  - name: Customer\n    type: Customer\n  - name: DisplayName\n    type: String\n    expression: Customer.Name", new()
        {
            ["Customer/Entity.yaml"] = "name: Customer\nproperties:\n  - name: Name\n    type: String"
        }).WorkflowDefinitions[WorkflowDefinition];
        var definition = _modelService.WorkflowDefinitions[WorkflowDefinition];
        definition.Properties.AddRange(computed.Properties);
        definition.ComputedProperties.AddRange(computed.ComputedProperties);
        _modelService.WorkflowDefinitions["Customer"] = ((ReferencePropertyDefinition)computed.Properties[0]).WorkflowDefinition!;
        var customerId = ObjectId.GenerateNewId().ToString();
        MockInstances(new Dictionary<string, BsonValue> { ["_id"] = ObjectId.GenerateNewId(), ["Customer"] = customerId });
        _workflowInstanceRepoMock.Setup(repository => repository.GetAllById(new[] { customerId },
                It.IsAny<Dictionary<string, string>>(), _ct))
            .ReturnsAsync([new() { ["_id"] = customerId, ["Name"] = "Customer name" }]);

        var result = await CreateController().GetInstances(WorkflowDefinition, ["DisplayName"], _ct);
        var rows = Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object>>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Customer name", Assert.Single(rows)["displayName"]);
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

    [Fact]
    public async Task GetCorrespondence_ReturnsEntriesFromMailLog()
    {
        MockCurrentUser("SystemAdmin");
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: WorkflowDefinition, currentStep: "Upload")
            .Build();
        MockInstance(instance);

        var timestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var entry = new MailLogEntry
        {
            Id = ObjectId.GenerateNewId().ToString(),
            WorkflowInstanceId = instance.Id,
            WorkflowDefinition = WorkflowDefinition,
            ExecutedBy = ObjectId.GenerateNewId().ToString(),
            Timestamp = timestamp,
            Subject = "Your submission was received",
            Body = "Thanks for submitting",
            To = [new MailLogRecipient("student@uva.nl", "Student Name")],
            Cc = [new MailLogRecipient("cc@uva.nl")],
            Bcc = [],
            Attachments = [new ArtifactInfo("artifact-1", "receipt.pdf", "application/pdf")]
        };
        MockMailLog(instance.Id, entry);

        var result = await CreateController().GetCorrespondence(instance.Id, _ct);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<CorrespondenceDto>>(ok.Value);
        var dto = Assert.Single(dtos);
        Assert.Equal(entry.Id, dto.Id);
        Assert.Equal("Your submission was received", dto.Subject);
        Assert.Equal(timestamp, dto.Timestamp);
        Assert.Equal("Thanks for submitting", dto.Body);
        Assert.Equal(["receipt.pdf"], dto.Attachments);

        Assert.Equal(2, dto.Recipients.Length);
        var toRecipient = Assert.Single(dto.Recipients, r => r.Type == CorrespondenceRecipientType.To);
        Assert.Equal("student@uva.nl", toRecipient.Email);
        Assert.Equal("Student Name", toRecipient.Name);
        var ccRecipient = Assert.Single(dto.Recipients, r => r.Type == CorrespondenceRecipientType.Cc);
        Assert.Equal("cc@uva.nl", ccRecipient.Email);
    }

    [Fact]
    public async Task GetCorrespondence_UnknownInstance_ReturnsNotFound()
    {
        MockCurrentUser("SystemAdmin");
        _workflowInstanceRepoMock
            .Setup(r => r.GetById("missing-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowInstance?)null);

        var result = await CreateController().GetCorrespondence("missing-id", _ct);

        var notFound = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(404, notFound.StatusCode);
        _mailLogRepositoryMock.Verify(
            r => r.GetByInstance(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetCorrespondence_WithoutViewCorrespondencePermission_ReturnsForbidden()
    {
        // No roles granted, so ViewCorrespondence is not allowed.
        MockCurrentUser();
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: WorkflowDefinition, currentStep: "Upload")
            .Build();
        MockInstance(instance);

        var result = await CreateController().GetCorrespondence(instance.Id, _ct);

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, forbidden.StatusCode);
        _mailLogRepositoryMock.Verify(
            r => r.GetByInstance(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
