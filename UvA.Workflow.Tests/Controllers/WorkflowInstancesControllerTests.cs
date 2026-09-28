using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Api.WorkflowInstances;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Notifications;
using UvA.Workflow.Persistence;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public class WorkflowInstancesControllerTests : ControllerTestsBase
{
    private const string WorkflowDefinition = "Project";

    private WorkflowInstancesController CreateController()
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