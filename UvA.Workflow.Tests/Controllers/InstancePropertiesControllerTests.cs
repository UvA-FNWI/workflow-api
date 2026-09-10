using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using System.Text.Json;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowInstances;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Journaling;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public class InstancePropertiesControllerTests : ControllerTestsBase
{
    private readonly AnswerService _answerService;
    private readonly AnswerConversionService _answerConversionService;
    private readonly List<PropertyChangeEntry> _loggedChanges = [];

    public InstancePropertiesControllerTests()
    {
        _answerConversionService = new AnswerConversionService(_userServiceMock.Object, _userRepoMock.Object);
        _answerService = new AnswerService(
            _modelService,
            _instanceService,
            _rightsService,
            _artifactServiceMock.Object,
            _answerConversionService,
            _workflowInstanceService,
            _instanceEventService.Object,
            _instanceJournalServiceMock.Object,
            _userServiceMock.Object,
            _externalUserServiceMock.Object);

        _workflowInstanceRepoMock
            .Setup(r => r.UpdateFields(It.IsAny<string>(), It.IsAny<UpdateDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _instanceJournalServiceMock
            .Setup(s => s.LogPropertyChange(It.IsAny<string>(), It.IsAny<PropertyChangeEntry>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, PropertyChangeEntry, CancellationToken>((_, entry, _) => _loggedChanges.Add(entry))
            .ReturnsAsync(false);
    }

    private (WorkflowInstancesController Controller, WorkflowInstance Instance) Build(
        string role, string? impersonatedRole = null, WorkflowInstance? instance = null)
    {
        instance ??= new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithProperties(("Title", b => b.Value("My Thesis")))
            .Build();

        MockInstance(instance);
        MockEmptyEventLog(instance);
        MockEmptyRelatedInstanceLookups();
        MockCurrentUser(role);

        var impersonationContext = new Mock<IImpersonationContextService>();
        impersonationContext.Setup(s => s.GetImpersonatedRole(instance, It.IsAny<CancellationToken>()))
            .ReturnsAsync(impersonatedRole);
        var rightsService = new RightsService(
            _modelService, _userServiceMock.Object, _workflowInstanceRepoMock.Object, impersonationContext.Object);
        var controller = new WorkflowInstancesController(
            _userServiceMock.Object,
            _workflowInstanceService,
            rightsService,
            null!,
            _workflowInstanceRepoMock.Object,
            _instanceService,
            _answerConversionService,
            _answerService,
            _modelService,
            null!,
            _eduIdUserServiceMock.Object);

        return (controller, instance);
    }

    private static SaveInstancePropertyRequest Value(object? value)
        => new(JsonSerializer.SerializeToElement(value));

    [Fact]
    public async Task GetProperties_ReturnsEveryPropertyWithItsValue()
    {
        var (controller, instance) = Build("SystemAdmin");

        var result = await controller.GetProperties(instance.Id, _ct);

        var dto = Assert.IsType<InstancePropertiesDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        // Title is not part of a form on this step.
        Assert.Contains(dto.Properties, p => p.Name == "Title");
        Assert.Equal("My Thesis", dto.Values["Title"]?.GetString());
        // An unset property is still listed, so an admin can fill it in.
        Assert.Contains(dto.Properties, p => p.Name == "EC");
        Assert.Null(dto.Values["EC"]);
    }

    [Fact]
    public async Task GetProperties_IncludesStepPropertiesAndTheirNestedChildren()
    {
        var (controller, instance) = Build("SystemAdmin");

        var result = await controller.GetProperties(instance.Id, _ct);
        var dto = Assert.IsType<InstancePropertiesDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        // Step-level embedded properties are included.
        var assessment = Assert.Single(dto.Properties, p => p.Name == "AssessmentReviewer");
        Assert.NotNull(assessment.SubProperties);
        Assert.Contains(assessment.SubProperties, p => p.Name == "Consent");
        // Nested values use dotted paths.
        Assert.True(dto.Values.ContainsKey("AssessmentReviewer.Consent"));
    }

    [Fact]
    public async Task GetProperties_ForbiddenForNonAdmin()
    {
        var (controller, instance) = Build("Student");

        var result = await controller.GetProperties(instance.Id, _ct);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task GetPropertyChoices_ReturnsReferenceTargets_WithoutASubmission()
    {
        var course = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Context", currentStep: "Start")
            .WithProperties(("Name", b => b.Value("Introduction to AI")))
            .Build();
        _workflowInstanceRepoMock
            .Setup(r => r.GetByWorkflowDefinition(
                "Context",
                It.IsAny<FilterDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([course]);
        var (controller, instance) = Build("Coordinator");
        var result = await controller.GetPropertyChoices(instance.Id, "Course", _ct);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var choices = Assert.IsAssignableFrom<IEnumerable<ChoiceDto>>(ok.Value);
        var choice = Assert.Single(choices);
        Assert.Equal(course.Id, choice.Name);
    }

    [Fact]
    public async Task GetPropertyChoices_ForbiddenForNonAdmin()
    {
        var (controller, instance) = Build("Student");
        var result = await controller.GetPropertyChoices(instance.Id, "Course", _ct);
        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task GetPropertyChoices_EmptyForNonReferenceProperty()
    {
        var (controller, instance) = Build("SystemAdmin");
        var result = await controller.GetPropertyChoices(instance.Id, "Title", _ct);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<ChoiceDto>>(ok.Value));
    }

    [Fact]
    public async Task GetPropertyChoices_NotFoundForUnknownProperty()
    {
        var (controller, instance) = Build("SystemAdmin");
        var result = await controller.GetPropertyChoices(instance.Id, "NoSuchProperty", _ct);
        Assert.Equal(404, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task GetPropertyChoices_NotFoundForUnknownInstance()
    {
        var (controller, _) = Build("SystemAdmin");
        var result = await controller.GetPropertyChoices("missing", "Course", _ct);
        Assert.Equal(404, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task SaveProperty_ForbiddenForNonAdmin()
    {
        var (controller, instance) = Build("Student");

        var result = await controller.SaveProperty(instance.Id, "Title", Value("Hacked"), _ct);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal("My Thesis", instance.GetProperty("Title")?.AsString);
    }

    [Fact]
    public async Task SaveProperty_AllowsPropertyEditor()
    {
        var (controller, instance) = Build("Coordinator");

        var result = await controller.SaveProperty(instance.Id, "Title", Value("Corrected title"), _ct);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("Corrected title", instance.GetProperty("Title")?.AsString);
    }

    [Fact]
    public async Task PropertyRoutes_UseRealAdminRoleWhileImpersonating()
    {
        var (controller, instance) = Build("SystemAdmin", "Student");

        Assert.IsType<OkObjectResult>((await controller.GetProperties(instance.Id, _ct)).Result);
        Assert.IsType<NoContentResult>(
            await controller.SaveProperty(instance.Id, "Title", Value("Corrected title"), _ct));
    }

    [Fact]
    public async Task SaveProperty_WritesValueAndJournalsEvenThoughNothingWasEverSubmitted()
    {
        var (controller, instance) = Build("SystemAdmin");

        var result = await controller.SaveProperty(instance.Id, "Title", Value("Corrected title"), _ct);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("Corrected title", instance.GetProperty("Title")?.AsString);

        // Admin edits are journalled before any form submission.
        var change = Assert.Single(_loggedChanges);
        Assert.Equal("Title", change.Path);
        Assert.Equal("My Thesis", change.OldValue?.AsString);
    }

    [Fact]
    public async Task SaveProperty_WritesNestedValueAndJournalsTheFullPath()
    {
        var (controller, instance) = Build("SystemAdmin");

        var result = await controller.SaveProperty(instance.Id, "AssessmentReviewer.Consent", Value("Yes"), _ct);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("Yes", instance.GetProperty("AssessmentReviewer", "Consent")?.AsString);

        // Journal paths include the parent.
        var change = Assert.Single(_loggedChanges);
        Assert.Equal("AssessmentReviewer.Consent", change.Path);
    }

    [Fact]
    public async Task SaveProperty_RejectsPathsDeeperThanOneLevel()
    {
        var (controller, instance) = Build("SystemAdmin");

        var result = await controller.SaveProperty(instance.Id, "A.B.C", Value("x"), _ct);

        Assert.Equal(400, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Empty(_loggedChanges);
    }

    [Theory]
    [InlineData("NoSuchProperty")]
    [InlineData("AssessmentReviewer.Nope")]
    // A scalar parent must return 404 rather than throw.
    [InlineData("Title.Nope")]
    public async Task SaveProperty_NotFoundForUnresolvablePath(string path)
    {
        var (controller, instance) = Build("SystemAdmin");

        var result = await controller.SaveProperty(instance.Id, path, Value("x"), _ct);

        Assert.Equal(404, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task SaveProperty_UnchangedValueIsNotJournalled()
    {
        var (controller, instance) = Build("SystemAdmin");

        await controller.SaveProperty(instance.Id, "Title", Value("My Thesis"), _ct);

        Assert.Empty(_loggedChanges);
    }

    [Fact]
    public async Task AssignRelatedUser_UsesTheRelatedUserProperty()
    {
        var user = new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserName = "reader",
            DisplayName = "Reader",
            Email = "reader@example.com"
        };
        _userServiceMock.Setup(service => service.GetUser(user.UserName, _ct)).ReturnsAsync(user);
        var (controller, instance) = Build("Student");
        var input = new AssignRelatedUserRequest(JsonSerializer.SerializeToElement(new
        {
            user.UserName,
            user.DisplayName,
            user.Email
        }));

        var result = await controller.AssignRelatedUser(instance.Id, "SecondReader", input, _ct);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(user.Id, instance.GetProperty("SecondReader")?.AsBsonDocument["_id"].ToString());
        Assert.Equal("SecondReader", Assert.Single(_loggedChanges).Path);
    }

    [Fact]
    public async Task RemoveRelatedUser_RemovesOnlyTheRequestedUser()
    {
        var keepId = ObjectId.GenerateNewId();
        var removeId = ObjectId.GenerateNewId();
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithProperties(("PracticalSupervisor", _ => new BsonArray
            {
                new BsonDocument("_id", keepId),
                new BsonDocument("_id", removeId)
            }))
            .Build();
        var (controller, _) = Build("Student", instance: instance);

        var result = await controller.RemoveRelatedUser(
            instance.Id, "PracticalSupervisor", removeId.ToString(), _ct);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(keepId, Assert.Single(instance.GetProperty("PracticalSupervisor")!.AsBsonArray)
            .AsBsonDocument["_id"].AsObjectId);
        Assert.Equal("PracticalSupervisor", Assert.Single(_loggedChanges).Path);
    }
}