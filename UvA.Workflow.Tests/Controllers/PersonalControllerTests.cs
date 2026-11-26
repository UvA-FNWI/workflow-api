using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using UvA.Workflow.Api.Personal;
using UvA.Workflow.Api.Personal.Dtos;
using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.Users.Dtos;
using UvA.Workflow.Events;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests.Controllers;

public partial class PersonalControllerTests : ControllerTestsBase
{
    private readonly PersonalInstanceService _personalInstanceService;
    private readonly PersonalController _controller;

    public PersonalControllerTests()
    {
        _personalInstanceService = new PersonalInstanceService(
            _modelService, _instanceService, _workflowInstanceRepoMock.Object, _rightsService);
        var authorizationFilterService = new InstanceAuthorizationFilterService(
            _rightsService, _modelService, _userServiceMock.Object, _workflowInstanceRepoMock.Object);
        _controller = new PersonalController(_userServiceMock.Object, _userRepoMock.Object,
            _personalInstanceService, authorizationFilterService);
        _workflowInstanceRepoMock.Setup(repository => repository.GetAllByType(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<BsonDocument?>(), _ct))
            .ReturnsAsync([]);
    }

    [Fact]
    public async Task GetMyInstances_ReturnsDirectUserInstancesTheUserCanView()
    {
        MockCurrentUser();
        var userId = ObjectId.Parse(UnitTestsHelpers.AdminUser.Id);
        var courseId = ObjectId.GenerateNewId();
        var createdOn = new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);
        var project = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Upload")
            .WithProperties(
                ("Title", property => property.Value("Personal project")),
                ("Course", property => property.Value(courseId.ToString())),
                ("Student", property => property.Value(
                    UserDocument(ObjectId.GenerateNewId(), "Student Name"))),
                ("Supervisor", property => property.Value(
                    UserDocument(userId, "Current Employee"))),
                ("Reviewer", property => property.Value(
                    UserDocument(userId, "Current Employee"))),
                ("Examiner", property => property.Value(
                    UserDocument(userId, "Current Employee"))),
                ("SecondAssessor", property => property.Value(
                    UserDocument(ObjectId.GenerateNewId(), "Second Assessor Name"))))
            .Build();
        project.CreatedOn = createdOn;

        FilterDefinition<WorkflowInstance>? capturedFilter = null;
        _workflowInstanceRepoMock
            .Setup(repository => repository.GetByFilter(
                It.IsAny<FilterDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<WorkflowInstance>, CancellationToken>((filter, _) =>
                capturedFilter = filter)
            .ReturnsAsync([project]);
        _workflowInstanceRepoMock
            .Setup(repository => repository.GetAllById(
                It.Is<string[]>(ids => ids.SequenceEqual(new[] { courseId.ToString() })),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Dictionary<string, BsonValue>
                {
                    ["_id"] = courseId,
                    ["Name"] = "Software Engineering"
                }
            ]);
        var result = await _controller.GetMyInstances(_ct);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PersonalInstancesDto>(ok.Value);
        Assert.Equal(UserDto.Create(UnitTestsHelpers.AdminUser), response.Person);
        var rows = response.Instances;
        var projectRow = Assert.Single(rows);
        Assert.Equal(["Supervisor", "Reviewer"], response.Roles.Select(role => role.Name));
        Assert.Equal("Begeleider", response.Roles[0].Title.Nl);
        Assert.Equal("Academisch begeleider", response.Roles[1].Title.Nl);

        Assert.Equal(project.Id, projectRow.Id);
        Assert.Equal("Project", projectRow.WorkflowDefinition);
        Assert.Equal("Personal project", projectRow.Title);
        Assert.Equal("Upload", projectRow.CurrentStep);
        Assert.False(projectRow.WorkflowStopped);
        Assert.Equal("Final version", projectRow.Progress.Text.En);
        Assert.Equal("Eindversie", projectRow.Progress.Text.Nl);
        Assert.Equal(StatusColor.Green, projectRow.Progress.Color);
        Assert.Equal(createdOn, projectRow.CreatedOn);
        Assert.Equal(["Reviewer", "Supervisor"], projectRow.Roles);
        Assert.Equal("Student Name", projectRow.Student);
        Assert.Equal("Software Engineering", projectRow.Course);
        Assert.Collection(projectRow.Staff,
            staff =>
            {
                Assert.Equal("Current Employee", staff.DisplayName);
                Assert.Equal(
                    [
                        ("Supervisor", "Begeleider"),
                        ("Examiner", "Examinator"),
                        ("Reviewer", "Beoordelaar")
                    ],
                    staff.Roles.Select(role => (role.En, role.Nl)));
                Assert.Null(staff.Organisation);
            },
            staff =>
            {
                Assert.Equal("Second Assessor Name", staff.DisplayName);
                Assert.Equal(
                    [("Second assessor", "Tweede beoordelaar")],
                    staff.Roles.Select(role => (role.En, role.Nl)));
                Assert.Null(staff.Organisation);
            });

        _workflowInstanceRepoMock.Verify(repository => repository.GetByFilter(
            It.IsAny<FilterDefinition<WorkflowInstance>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _workflowInstanceRepoMock.Verify(repository => repository.GetAllById(
            It.IsAny<string[]>(),
            It.Is<Dictionary<string, string>>(projection =>
                projection.Count == 1 && projection["Name"] == "$Properties.Name"),
            It.IsAny<CancellationToken>()), Times.Once);

        var renderedFilter = RenderFilter(capturedFilter!);
        AssertDirectPropertyFilter(renderedFilter, "Project", "Properties.Student._id", userId);
        AssertDirectPropertyFilter(renderedFilter, "Project", "Properties.Supervisor._id", userId);
        AssertDirectPropertyFilter(renderedFilter, "Project", "Properties.Reviewer._id", userId);
        AssertDefinitionIsExcluded(renderedFilter, "Context");
    }

    [Theory]
    [InlineData("Upload")]
    [InlineData(null)]
    public async Task PersonalInstanceService_GetInstances_ReturnsComputedStoppedProgressUntilRecovery(
        string? currentStep)
    {
        var userId = ObjectId.Parse(UnitTestsHelpers.AdminUser.Id);
        var stoppedAt = new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);
        var definition = _modelService.WorkflowDefinitions["Project"];
        definition.StopWhen = new Condition
        {
            Logical = new Logical
            {
                Operator = LogicalOperator.Or,
                Children = [new Condition { Event = "Stop" }, new Condition { Event = "Cancel" }]
            }
        };
        definition.Events.Add(new EventDefinition { Name = "Continue", Suppresses = ["Stop"] });

        var project = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Upload")
            .WithEvent("Stop", stoppedAt)
            .WithEvent("Audit", stoppedAt.AddDays(1))
            .WithProperties(("Supervisor", property => property.Value(UserDocument(userId, "Employee"))))
            .Build();
        project.CurrentStep = currentStep;
        _workflowInstanceRepoMock
            .Setup(repository => repository.GetByFilter(
                It.IsAny<FilterDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([project]);

        var stopped =
            Assert.Single((await _personalInstanceService.GetInstances(UnitTestsHelpers.AdminUser, _ct)).Instances);
        Assert.True(stopped.WorkflowStopped);
        Assert.Equal(currentStep, stopped.CurrentStep);
        Assert.Equal("Stopped (27/07)", stopped.Progress.Text.En);
        Assert.Equal("Gestopt (27/07)", stopped.Progress.Text.Nl);
        Assert.Equal(StatusColor.Red, stopped.Progress.Color);

        project.Events["Continue"] = new InstanceEvent { Id = "Continue", Date = stoppedAt.AddDays(2) };

        var recovered =
            Assert.Single((await _personalInstanceService.GetInstances(UnitTestsHelpers.AdminUser, _ct)).Instances);
        Assert.False(recovered.WorkflowStopped);
        Assert.Equal(currentStep, recovered.CurrentStep);
        Assert.Equal(StatusColor.Green, recovered.Progress.Color);
    }

    [Fact]
    public async Task PersonalInstanceService_GetInstances_UsesFirstMatchingStopMessageWithItsEventDate()
    {
        _modelService.WorkflowDefinitions["Project"].StopWhen = new Condition
        {
            Logical = new Logical
            {
                Operator = LogicalOperator.Or,
                Children =
                [
                    new Condition
                    {
                        Event = "Cancel",
                        Message = new BilingualString(
                            "Cancelled ({{ dateShort(CancelEvent) }})",
                            "Geannuleerd ({{ dateShort(CancelEvent) }})")
                    },
                    new Condition
                    {
                        Event = "Pause",
                        Message = new BilingualString(
                            "Paused ({{ dateShort(PauseEvent) }})",
                            "Gepauzeerd ({{ dateShort(PauseEvent) }})")
                    }
                ]
            }
        };
        var userId = ObjectId.Parse(UnitTestsHelpers.AdminUser.Id);
        var pausedAt = new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);
        var project = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Upload")
            .WithEvent("Pause", pausedAt)
            .WithProperties(("Supervisor", property => property.Value(UserDocument(userId, "Employee"))))
            .Build();
        _workflowInstanceRepoMock
            .Setup(repository => repository.GetByFilter(
                It.IsAny<FilterDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([project]);

        await AssertStoppedProgress("Pause", "Paused (27/07)", "Gepauzeerd (27/07)");
        project.Events["Cancel"] = new InstanceEvent { Id = "Cancel", Date = pausedAt.AddDays(-1) };
        await AssertStoppedProgress("Cancel", "Cancelled (26/07)", "Geannuleerd (26/07)");

        async Task AssertStoppedProgress(string eventId, string english, string dutch)
        {
            var stopped = Assert.Single(
                (await _personalInstanceService.GetInstances(UnitTestsHelpers.AdminUser, _ct)).Instances);
            Assert.True(stopped.WorkflowStopped);
            Assert.Equal(english, stopped.Progress.Text.En);
            Assert.Equal(dutch, stopped.Progress.Text.Nl);
            Assert.Equal(StatusColor.Red, stopped.Progress.Color);
            Assert.Equal(eventId, stopped.Progress.EventId);
        }
    }

    [Fact]
    public async Task PersonalInstanceService_GetInstances_CombinesPersonalAndProgressRelatedLookups()
    {
        var userId = ObjectId.Parse(UnitTestsHelpers.AdminUser.Id);
        var courseId = ObjectId.GenerateNewId();
        var uploadStep = _modelService.WorkflowDefinitions["Project"].AllSteps
            .Single(step => step.Name == "Upload");
        uploadStep.Progress =
        [
            new ProgressInformation
            {
                Text = new BilingualString(
                    "Working on {{ Course.Type }}",
                    "Werkt aan {{ Course.Type }}")
            }
        ];
        var project = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Upload")
            .WithProperties(
                ("Course", property => property.Value(courseId.ToString())),
                ("Supervisor", property => property.Value(
                    UserDocument(userId, "Current Employee"))))
            .Build();

        _workflowInstanceRepoMock
            .Setup(repository => repository.GetByFilter(
                It.IsAny<FilterDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([project]);
        _workflowInstanceRepoMock
            .Setup(repository => repository.GetAllById(
                It.Is<string[]>(ids => ids.SequenceEqual(new[] { courseId.ToString() })),
                It.Is<Dictionary<string, string>>(projection =>
                    projection.Count == 2 &&
                    projection["Name"] == "$Properties.Name" &&
                    projection["Type"] == "$Type"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Dictionary<string, BsonValue>
                {
                    ["_id"] = courseId,
                    ["Name"] = "Software Engineering",
                    ["Type"] = "Course"
                }
            ]);

        var result = await _personalInstanceService.GetInstances(UnitTestsHelpers.AdminUser, _ct);

        var progress = Assert.Single(result.Instances).Progress;
        Assert.Equal("Working on Course", progress.Text.En);
        Assert.Equal("Werkt aan Course", progress.Text.Nl);
        _workflowInstanceRepoMock.Verify(repository => repository.GetAllById(
            It.IsAny<string[]>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PersonalInstanceService_GetInstances_BatchesSharedReferencesAcrossWorkflowDefinitions()
    {
        var userId = ObjectId.Parse(UnitTestsHelpers.AdminUser.Id);
        var projectCourseId = ObjectId.GenerateNewId();
        var rmssCourseId = ObjectId.GenerateNewId();
        var project = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Upload")
            .WithProperties(
                ("Course", property => property.Value(projectCourseId.ToString())),
                ("Supervisor", property => property.Value(
                    UserDocument(userId, "Current Employee"))))
            .Build();
        var rmssProject = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project-RMSS")
            .WithCurrentStep("Start")
            .WithProperties(
                ("Course", property => property.Value(rmssCourseId.ToString())),
                ("Supervisor", property => property.Value(
                    UserDocument(userId, "Current Employee"))))
            .Build();

        _workflowInstanceRepoMock
            .Setup(repository => repository.GetByFilter(
                It.IsAny<FilterDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([project, rmssProject]);
        _workflowInstanceRepoMock
            .Setup(repository => repository.GetAllById(
                It.Is<string[]>(ids => ids.ToHashSet().SetEquals(
                    new[] { projectCourseId.ToString(), rmssCourseId.ToString() })),
                It.Is<Dictionary<string, string>>(projection =>
                    projection.Count == 1 && projection["Name"] == "$Properties.Name"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Dictionary<string, BsonValue>
                {
                    ["_id"] = projectCourseId,
                    ["Name"] = "Software Engineering"
                },
                new Dictionary<string, BsonValue>
                {
                    ["_id"] = rmssCourseId,
                    ["Name"] = "Research Master"
                }
            ]);

        var result = await _personalInstanceService.GetInstances(UnitTestsHelpers.AdminUser, _ct);

        Assert.Equal(2, result.Instances.Length);
        Assert.Contains(result.Instances, instance => instance.Course == "Software Engineering");
        Assert.Contains(result.Instances, instance => instance.Course == "Research Master");
        _workflowInstanceRepoMock.Verify(repository => repository.GetAllById(
            It.IsAny<string[]>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMyInstances_ReturnsCurrentUserWithoutAssignedInstances()
    {
        MockCurrentUser();
        _workflowInstanceRepoMock.Setup(repository => repository.GetByFilter(
                It.IsAny<FilterDefinition<WorkflowInstance>>(), _ct))
            .ReturnsAsync([]);

        var result = await _controller.GetMyInstances(_ct);

        var response = Assert.IsType<PersonalInstancesDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(UserDto.Create(UnitTestsHelpers.AdminUser), response.Person);
        Assert.Empty(response.Roles);
        Assert.Empty(response.Instances);
    }

    [Fact]
    public async Task GetMyInstances_WithoutCurrentUser_ReturnsUnauthorizedWithoutQueryingMongo()
    {
        _userServiceMock
            .Setup(service => service.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        var result = await _controller.GetMyInstances(_ct);

        Assert.IsType<UnauthorizedResult>(result.Result);
        _workflowInstanceRepoMock.Verify(repository => repository.GetByFilter(
            It.IsAny<FilterDefinition<WorkflowInstance>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetUserInstances_OnlyReturnsInstancesVisibleToTheViewer(bool includeVisibleInstance)
    {
        MockCurrentUser("Coordinator");
        _modelService.WorkflowDefinitions["Project"].Roles.Single(role => role.Name == "Coordinator")
            .Actions.RemoveAll(action => action.Type == RoleAction.View);

        var person = new User
        {
            Id = ObjectId.GenerateNewId().ToString(), DisplayName = "Practical Supervisor",
            Email = "supervisor@example.org", UserName = "supervisor"
        };
        var linkedPerson = new BsonArray { InstanceUser.FromUser(person).ToBsonDocument() };
        var hidden = new WorkflowInstanceBuilder().With("Project", "Start")
            .WithProperties(("PracticalSupervisor", property => property.Value(linkedPerson)))
            .Build();
        var visible = new WorkflowInstanceBuilder().With("Project", "Start")
            .WithProperties(
                ("PracticalSupervisor", property => property.Value(linkedPerson)),
                ("Supervisor", property => property.Value(
                    InstanceUser.FromUser(UnitTestsHelpers.AdminUser).ToBsonDocument())))
            .Build();
        _userRepoMock.Setup(repository => repository.GetById(person.Id, _ct)).ReturnsAsync(person);
        _workflowInstanceRepoMock.Setup(repository => repository.GetByFilter(
                It.IsAny<FilterDefinition<WorkflowInstance>>(), _ct))
            .ReturnsAsync(includeVisibleInstance ? [hidden, visible] : [hidden]);

        var result = await _controller.GetUserInstances(person.Id, _ct);

        if (!includeVisibleInstance)
        {
            Assert.Equal(404, Assert.IsType<ObjectResult>(result.Result).StatusCode);
            return;
        }

        var response = Assert.IsType<PersonalInstancesDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(UserDto.Create(person), response.Person);
        var instance = Assert.Single(response.Instances);
        Assert.Equal(visible.Id, instance.Id);
        Assert.Equal(["PracticalSupervisor"], instance.Roles);
    }

    private static BsonDocument UserDocument(ObjectId id, string displayName) => new()
    {
        ["_id"] = id,
        ["UserName"] = id.ToString(),
        ["DisplayName"] = displayName
    };

    private static void AssertDirectPropertyFilter(
        BsonDocument filter,
        string workflowDefinition,
        string userPath,
        ObjectId userId)
    {
        var expectedMatch = new BsonDocument("$in", new BsonArray { userId });
        var found = filter["$or"].AsBsonArray
            .Select(branch => branch.AsBsonDocument)
            .Any(branch =>
                Descendants(branch).Any(condition =>
                    condition.GetValue("WorkflowDefinition", BsonNull.Value) == workflowDefinition) &&
                Descendants(branch).Any(condition =>
                    condition.GetValue(userPath, BsonNull.Value) == expectedMatch));

        Assert.True(found, $"No query branch found for {workflowDefinition}.{userPath}");
    }

    private static void AssertDefinitionIsExcluded(BsonDocument filter, string workflowDefinition)
    {
        var found = filter["$or"].AsBsonArray
            .Select(value => value.AsBsonDocument)
            .Any(branch => Descendants(branch).Any(condition =>
                condition.GetValue("WorkflowDefinition", BsonNull.Value) == workflowDefinition));

        Assert.False(found, $"The {workflowDefinition} query branch should not be present");
    }
}