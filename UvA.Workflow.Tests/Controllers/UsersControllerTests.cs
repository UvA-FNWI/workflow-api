using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using MongoDB.Driver;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Personal;
using UvA.Workflow.Api.Personal.Dtos;
using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.Users;
using UvA.Workflow.Api.Users.Dtos;
using UvA.Workflow.Organizations;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests.Controllers;

public class UsersControllerTests : ControllerTestsBase
{
    private const string InstanceId = "instance-id";
    private const string ExternalUserId = "665f35fb3f1b3c6d4b3d0f12";

    private readonly AnswerService _answerService;
    private readonly ExternalUserEmailUpdateService _externalUserEmailUpdateService;
    private readonly PersonalInstanceService _personalInstanceService;
    private readonly InstanceAuthorizationFilterService _authorizationFilterService;

    public UsersControllerTests()
    {
        _answerService = new AnswerService(
            _modelService,
            _instanceService,
            _rightsService,
            _artifactServiceMock.Object,
            new AnswerConversionService(_userServiceMock.Object, _userRepoMock.Object),
            _workflowInstanceService,
            _instanceEventService.Object,
            _instanceJournalServiceMock.Object,
            _userServiceMock.Object,
            _externalUserServiceMock.Object);
        _externalUserEmailUpdateService = new ExternalUserEmailUpdateService(
            _rightsService,
            _answerService,
            _modelService,
            _instanceService);
        _personalInstanceService = new PersonalInstanceService(
            _modelService, _instanceService, _workflowInstanceRepoMock.Object, _rightsService);
        _authorizationFilterService = new InstanceAuthorizationFilterService(
            _rightsService, _modelService, _userServiceMock.Object, _workflowInstanceRepoMock.Object);
        _workflowInstanceRepoMock.Setup(repository => repository.GetAllByType(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<BsonDocument?>(), _ct))
            .ReturnsAsync([]);
    }

    [Theory]
    [InlineData("Coordinator")]
    [InlineData("Student")]
    [InlineData("Admin")]
    [InlineData("Api")]
    public async Task Users_GetLoggedInUser_Allow(string role)
    {
        // Arrange
        var controller = BuildControllerWithRoles([role]);
        // Act
        var result = await controller.GetLoggedInUser(_ct);
        //Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var userDto = Assert.IsType<UserDto>(okResult.Value);

        Assert.Equal(UnitTestsHelpers.AdminUser.Id, userDto.Id);
        Assert.Equal(UnitTestsHelpers.AdminUser.Email, userDto.Email);
        Assert.Equal(UnitTestsHelpers.AdminUser.UserName, userDto.UserName);
        Assert.Equal(UnitTestsHelpers.AdminUser.DisplayName, userDto.DisplayName);
    }

    [Theory]
    [InlineData("SystemAdmin", true)]
    [InlineData("Coordinator", false)]
    [InlineData("Student", false)]
    [InlineData("RandomPerson", false)]
    public async Task Users_GetLoggedInUser_SetsIsSuperAdmin_ForSystemAdminRole(string role, bool expectedIsSuperAdmin)
    {
        // Arrange
        var controller = BuildControllerWithRoles([role]);
        // Act
        var result = await controller.GetLoggedInUser(_ct);
        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var userDto = Assert.IsType<UserDto>(okResult.Value);
        Assert.Equal(expectedIsSuperAdmin, userDto.IsSuperAdmin);
    }

    [Fact]
    public async Task Users_SearchAndGetUserInstances_RequireOverviewAccess()
    {
        var controller = BuildControllerWithRoles([]);
        var directAssignment = new WorkflowInstanceBuilder().With("Project", "Start")
            .WithProperties(("Supervisor", property => property.Value(
                InstanceUser.FromUser(UnitTestsHelpers.AdminUser).ToBsonDocument())))
            .Build();
        Assert.True(await _rightsService.Can(directAssignment, RoleAction.View));

        var search = await controller.Search("example", _personalInstanceService, _authorizationFilterService, _ct);
        var detail = await controller.GetUserInstances(ObjectId.GenerateNewId().ToString(),
            _personalInstanceService, _authorizationFilterService, _ct);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(search.Result).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(detail.Result).StatusCode);
        _userRepoMock.Verify(repository => repository.SearchByPrefix(It.IsAny<string>(), _ct), Times.Never);
        _userRepoMock.Verify(repository => repository.GetById(It.IsAny<string>(), _ct), Times.Never);
    }

    [Fact]
    public async Task Users_Search_ReturnsOnlyPeopleLinkedToVisibleInstances()
    {
        var controller = BuildControllerWithRoles(["Coordinator"]);
        _modelService.WorkflowDefinitions["Project"].Roles.Single(role => role.Name == "Coordinator").Actions
            .RemoveAll(action => action.Type == RoleAction.View);

        var visible = SearchUser("visible");
        var hidden = SearchUser("hidden");
        var unlinked = SearchUser("unlinked");
        _userRepoMock.Setup(repository => repository.SearchByPrefix("example", _ct))
            .Returns(new[] { hidden, unlinked, visible }.ToAsyncEnumerable());
        _workflowInstanceRepoMock.Setup(repository => repository.GetByFilter(
                It.IsAny<FilterDefinition<WorkflowInstance>>(), _ct))
            .ReturnsAsync([
                new WorkflowInstanceBuilder().With("Project", "Start")
                    .WithProperties(("PracticalSupervisor", property => property.Value(
                        new BsonArray { InstanceUser.FromUser(hidden).ToBsonDocument() })))
                    .Build(),
                new WorkflowInstanceBuilder().With("Project", "Start")
                    .WithProperties(
                        ("PracticalSupervisor", property => property.Value(
                            new BsonArray { InstanceUser.FromUser(visible).ToBsonDocument() })),
                        ("Supervisor", property => property.Value(
                            InstanceUser.FromUser(UnitTestsHelpers.AdminUser).ToBsonDocument())))
                    .Build()
            ]);

        var result = await controller.Search("  example ", _personalInstanceService, _authorizationFilterService, _ct);

        var response = Assert.IsType<PeopleSearchDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(PersonDto.Create(visible), Assert.Single(response.People));
        Assert.False(response.HasMore);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(51)]
    public async Task Users_Search_CountsOnlyVisiblePeopleTowardsResultLimit(int visibleCount)
    {
        var controller = BuildControllerWithRoles(["Supervisor"]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools));
        var visible = Enumerable.Range(0, visibleCount).Select(index => SearchUser($"person-{index:D2}")).ToArray();
        var candidates = visible.Prepend(SearchUser("unlinked"));
        _userRepoMock.Setup(repository => repository.SearchByPrefix("example", _ct))
            .Returns(candidates.ToAsyncEnumerable());

        var linkedUsers = new BsonArray(visible.Reverse().Select(user => InstanceUser.FromUser(user).ToBsonDocument()));
        var instance = new WorkflowInstanceBuilder().With("Project", "Start")
            .WithProperties(("PracticalSupervisor", property => property.Value(linkedUsers)))
            .Build();
        _workflowInstanceRepoMock.Setup(repository => repository.GetByFilter(
                It.IsAny<FilterDefinition<WorkflowInstance>>(), _ct))
            .ReturnsAsync([instance]);

        var result = await controller.Search("example", _personalInstanceService, _authorizationFilterService, _ct);

        var response = Assert.IsType<PeopleSearchDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(visible.Take(50).Select(PersonDto.Create), response.People);
        Assert.Equal(visibleCount > 50, response.HasMore);
    }

    [Fact]
    public async Task Users_Search_SupportsCoordinatorsAssignedThroughCourses()
    {
        var controller = BuildControllerWithRoles([]);
        var course = new WorkflowInstanceBuilder().With("Course", "")
            .WithProperties(("Coordinator", property => property.Value(
                new BsonArray { InstanceUser.FromUser(UnitTestsHelpers.AdminUser).ToBsonDocument() })))
            .Build();
        var person = SearchUser("supervisor");
        var project = new WorkflowInstanceBuilder().With("Project", "Start")
            .WithProperties(
                ("Course", property => property.Value(course.Id)),
                ("Supervisor", property => property.Value(InstanceUser.FromUser(person).ToBsonDocument())))
            .Build();
        _workflowInstanceRepoMock.Setup(repository => repository.GetByIds(
                It.Is<IEnumerable<string>>(ids => ids.Contains(course.Id)), It.IsAny<CancellationToken>()))
            .ReturnsAsync([course]);
        _workflowInstanceRepoMock.Setup(repository => repository.GetAllByType(
                "Course", It.IsAny<Dictionary<string, string>>(),
                It.Is<BsonDocument?>(filter => filter != null &&
                                               filter.GetValue("Properties.Coordinator._id", BsonNull.Value) ==
                                               ObjectId.Parse(UnitTestsHelpers.AdminUser.Id)), _ct))
            .ReturnsAsync([new Dictionary<string, BsonValue> { ["_id"] = ObjectId.Parse(course.Id) }]);
        _workflowInstanceRepoMock.Setup(repository => repository.GetAllByType(
                "Project", It.IsAny<Dictionary<string, string>>(),
                It.Is<BsonDocument?>(filter => filter != null &&
                                               filter.GetValue("Properties.Course", BsonNull.Value) ==
                                               new BsonDocument("$in", new BsonArray { course.Id })), _ct))
            .ReturnsAsync([new Dictionary<string, BsonValue> { ["_id"] = ObjectId.Parse(project.Id) }]);
        _workflowInstanceRepoMock.Setup(repository => repository.GetByFilter(
                It.IsAny<FilterDefinition<WorkflowInstance>>(), _ct))
            .ReturnsAsync([project]);
        _userRepoMock.Setup(repository => repository.SearchByPrefix("supervisor", _ct))
            .Returns(new[] { person }.ToAsyncEnumerable());

        var result = await controller.Search("supervisor", _personalInstanceService, _authorizationFilterService, _ct);

        var response = Assert.IsType<PeopleSearchDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(PersonDto.Create(person), Assert.Single(response.People));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Users_GetUserInstances_OnlyReturnsInstancesVisibleToTheViewer(bool includeVisibleInstance)
    {
        var controller = BuildControllerWithRoles(["Coordinator"]);
        _modelService.WorkflowDefinitions["Project"].Roles.Single(role => role.Name == "Coordinator")
            .Actions.RemoveAll(action => action.Type == RoleAction.View);

        var personId = ObjectId.GenerateNewId();
        var person = new User
        {
            Id = personId.ToString(), DisplayName = "Practical Supervisor",
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

        var result = await controller.GetUserInstances(person.Id, _personalInstanceService,
            _authorizationFilterService, _ct);

        if (!includeVisibleInstance)
        {
            Assert.Equal(404, Assert.IsType<ObjectResult>(result.Result).StatusCode);
            return;
        }

        var response = Assert.IsType<PersonalPersonDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(person.Id, response.Person.Id);
        var instance = Assert.Single(response.Instances);
        Assert.Equal(visible.Id, instance.Id);
        Assert.Equal(["PracticalSupervisor"], instance.Roles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Users_GetUserInstances_OnlyReturnsAuthorizedProjectsThatInheritThePersonsRole(bool transitive)
    {
        var controller = BuildControllerWithRoles(["Coordinator"]);
        var (person, _, visibleProject) = MockInheritedAssignments(transitive);
        _userRepoMock.Setup(repository => repository.GetById(person.Id, _ct)).ReturnsAsync(person);

        var result = await controller.GetUserInstances(person.Id, _personalInstanceService,
            _authorizationFilterService, _ct);

        var response = Assert.IsType<PersonalPersonDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var instance = Assert.Single(response.Instances);
        Assert.Equal(visibleProject.Id, instance.Id);
        Assert.Equal([transitive ? "LocalAdmin" : "Coordinator"], instance.Roles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Users_Search_FindsPeopleWhoseOnlyVisibleAssignmentIsInherited(bool transitive)
    {
        var controller = BuildControllerWithRoles(["Coordinator"]);
        var (visiblePerson, hiddenPerson, _) = MockInheritedAssignments(transitive);
        _userRepoMock.Setup(repository => repository.SearchByPrefix("example", _ct))
            .Returns(new[] { hiddenPerson, visiblePerson }.ToAsyncEnumerable());
        var result = await controller.Search("example", _personalInstanceService, _authorizationFilterService, _ct);

        var response = Assert.IsType<PeopleSearchDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(PersonDto.Create(visiblePerson), Assert.Single(response.People));
    }

    private (User VisiblePerson, User HiddenPerson, WorkflowInstance VisibleProject)
        MockInheritedAssignments(bool transitive)
    {
        var definitions = _modelService.WorkflowDefinitions;
        definitions["Project"].Roles.Single(role => role.Name == "Coordinator")
            .Actions.RemoveAll(action => action.Type == RoleAction.View);
        var roleName = transitive ? "LocalAdmin" : "Coordinator";
        if (transitive)
            definitions["Programme"].Roles.RemoveAll(role => role.Name == roleName);

        var instances = new List<WorkflowInstance>();
        var visiblePerson = SearchUser("example-visible");
        var hiddenPerson = SearchUser("example-hidden");
        var sharedCourse = AddCourse(visiblePerson);
        var hiddenCourse = AddCourse(visiblePerson, hiddenPerson);
        var visibleProject = new WorkflowInstanceBuilder().With("Project", "Start")
            .WithProperties(
                ("Course", property => property.Value(sharedCourse.Id)),
                ("Supervisor", property => property.Value(
                    InstanceUser.FromUser(UnitTestsHelpers.AdminUser).ToBsonDocument())))
            .Build();
        var hiddenProject = new WorkflowInstanceBuilder().With("Project", "Start")
            .WithProperties(("Course", property => property.Value(hiddenCourse.Id)))
            .Build();
        instances.AddRange([visibleProject, hiddenProject]);
        string[] referenceProperties = ["Faculty", "Programme", "Course"];
        _workflowInstanceRepoMock.Setup(repository => repository.GetByIds(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) =>
                instances.Where(instance => ids.Contains(instance.Id)).ToArray());
        _workflowInstanceRepoMock.Setup(repository => repository.GetByFilter(
                It.IsAny<FilterDefinition<WorkflowInstance>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FilterDefinition<WorkflowInstance> filter, CancellationToken _) =>
            {
                var conditions = Descendants(RenderFilter(filter)).ToArray();

                bool Includes(string field, BsonValue value) => conditions.Any(condition =>
                    condition.GetValue(field, BsonNull.Value) is BsonDocument matcher &&
                    matcher.GetValue("$in", BsonNull.Value) is BsonArray values && values.Contains(value));

                return instances.Where(instance =>
                    (instance.GetProperty(roleName) is BsonArray users && users.Any(user =>
                        Includes($"Properties.{roleName}._id", user.AsBsonDocument["_id"]))) ||
                    referenceProperties.Any(name => instance.GetProperty(name) is BsonString reference &&
                                                    Includes($"Properties.{name}", reference))).ToArray();
            });
        MockEmptyRelatedInstanceLookups();

        return (visiblePerson, hiddenPerson, visibleProject);

        WorkflowInstance AddCourse(params User[] users)
        {
            var source = new WorkflowInstanceBuilder().With(transitive ? "Faculty" : "Course", "")
                .WithProperties((roleName, property => property.Value(
                    new BsonArray(users.Select(user => InstanceUser.FromUser(user).ToBsonDocument())))))
                .Build();
            instances.Add(source);
            if (!transitive)
                return source;

            var programme = new WorkflowInstanceBuilder().With("Programme", "")
                .WithProperties(("Faculty", property => property.Value(source.Id))).Build();
            var course = new WorkflowInstanceBuilder().With("Course", "")
                .WithProperties(("Programme", property => property.Value(programme.Id))).Build();
            instances.AddRange([programme, course]);
            return course;
        }
    }

    private static User SearchUser(string userName) => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        UserName = userName,
        DisplayName = $"Example {userName}",
        Email = $"{userName}@example.org"
    };

    [Theory]
    [InlineData("Student")]
    [InlineData("RandomPerson")]
    public async Task Users_Create_ThrowUnauthorizedException(string role)
    {
        // Arrange
        var controller = BuildControllerWithRoles([role]);
        // Act and Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.Create(new CreateUserDto("username", "displayName", "email@example.org"), _ct));
    }

    [Theory]
    [InlineData("Api")]
    [InlineData("Coordinator")]
    public async Task Users_Create_AllowedWithViewAdminRights(string role)
    {
        // Arrange
        var controller = BuildControllerWithRoles([role]);
        // Act
        var result = await controller.Create(new CreateUserDto("username", "displayName", "email@example.org"), _ct);
        //Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);

        Assert.Equal(nameof(UsersController.GetById), createdResult.ActionName);

        _userRepoMock.Verify(r => r.Create(It.Is<User>(u =>
            u.UserName == "username" &&
            u.DisplayName == "displayName" &&
            u.Email == "email@example.org"), _ct), Times.Once);
    }

    [Fact]
    public async Task Users_Create_ReturnsConflict_WhenEmailAlreadyExists()
    {
        var controller = BuildControllerWithRoles(["Api"]);
        _userRepoMock.Setup(r => r.GetByEmail("duplicate@example.org", _ct))
            .ReturnsAsync(new User { Email = "DUPLICATE@example.org" });

        var result = await controller.Create(new CreateUserDto("username",
            "displayName",
            "duplicate@example.org"), _ct);

        var conflictResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflictResult.StatusCode);
        var error = Assert.IsType<Error>(conflictResult.Value);
        Assert.Equal("ManualUserEmailAlreadyExists", error.ErrorCode);
        Assert.Equal("Email already exists", error.Message);
        _userRepoMock.Verify(r => r.Create(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("student@uva.nl")]
    [InlineData("student@sub.uva.nl")]
    public async Task Users_Create_RejectsInternalEmail(string email)
    {
        var controller = BuildControllerWithRoles(["Api"]);

        var result = await controller.Create(new CreateUserDto("username", "displayName", email), _ct);

        var badRequestResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
        var error = Assert.IsType<Error>(badRequestResult.Value);
        Assert.Equal("ManualUserInternalEmail", error.ErrorCode);
        Assert.Equal("Internal email address", error.Message);
        _userRepoMock.Verify(r => r.Create(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Users_Create_TrimsEmailBeforePersisting()
    {
        var controller = BuildControllerWithRoles(["Api"]);

        await controller.Create(new CreateUserDto("username", "displayName", "  doctor@amsterdamumc.nl  "), _ct);

        _userRepoMock.Verify(r => r.GetByEmail("doctor@amsterdamumc.nl", _ct), Times.Once);
        _userRepoMock.Verify(r => r.Create(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
        var createdUser = Assert.IsType<User>(_userRepoMock.Invocations
            .Single(i => i.Method.Name == nameof(IUserRepository.Create))
            .Arguments[0]);
        Assert.Equal("doctor@amsterdamumc.nl", createdUser.Email);
    }

    [Fact]
    public async Task Users_UpdateExternalUser_UpdatesRequiredExternalUser()
    {
        var user = new User
        {
            Id = ExternalUserId,
            UserName = "old@example.org",
            DisplayName = "External User",
            Email = "old@example.org",
            ProviderKey = "eduid",
            InvitationState = UserInvitationState.Required,
            IsActive = false
        };
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        _externalUserServiceMock
            .Setup(s => s.CreateOrUpdateExternalUser("External User", "new@example.org", null, user.Id, _ct))
            .Callback(() =>
            {
                user.Email = "new@example.org";
                user.UserName = "new@example.org";
            })
            .ReturnsAsync((UserSearchResult)null!);
        var controller = BuildControllerWithRoles(["Api"]);


        var result = await controller.UpdateExternalUser(user.Id,
            new UpdateUserEmailDto(new ExternalUserDto("External User", "new@example.org", null, user.Id),
                InstanceId), _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<UserDto>(okResult.Value);
        Assert.Equal("new@example.org", dto.Email);
        Assert.Equal("new@example.org", dto.UserName);
        Assert.True(dto.IsEmailEditable);
        _externalUserServiceMock.Verify(s => s.CreateOrUpdateExternalUser(
            "External User", "new@example.org", null, user.Id, _ct), Times.Once);
    }

    [Fact]
    public async Task Users_UpdateExternalUser_UpdatesEveryEditableUserReferenceInInstance()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var user = new User
        {
            Id = userId,
            UserName = "old@example.org",
            DisplayName = "External User",
            Email = "old@example.org",
            ProviderKey = "eduid",
            InvitationState = UserInvitationState.Required,
            IsActive = false
        };
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start", id: InstanceId)
            .WithProperties(
                ("Supervisor", _ => new BsonDocument
                {
                    { "_id", ObjectId.Parse(userId) },
                    { "UserName", "old@example.org" },
                    { "DisplayName", "External User" },
                    { "Email", "old@example.org" }
                }),
                ("Reviewer", _ => new BsonDocument
                {
                    { "_id", ObjectId.Parse(userId) },
                    { "UserName", "old@example.org" },
                    { "DisplayName", "External User" },
                    { "Email", "old@example.org" }
                }))
            .WithEvent("Start", DateTime.UtcNow)
            .Build();
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        _externalUserServiceMock
            .Setup(s => s.CreateOrUpdateExternalUser("External User", "new@example.org", null, user.Id, _ct))
            .Callback(() =>
            {
                user.Email = "new@example.org";
                user.UserName = "new@example.org";
            })
            .ReturnsAsync((UserSearchResult)null!);

        var controller = BuildControllerWithRoles(["Api"]);

        _workflowInstanceRepoMock.Setup(r => r.GetById(InstanceId, _ct)).ReturnsAsync(instance);
        _userServiceMock.Setup(s => s.GetUser("new@example.org", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await controller.UpdateExternalUser(user.Id,
            new UpdateUserEmailDto(new ExternalUserDto("External User", "new@example.org", null, user.Id),
                InstanceId), _ct);

        Assert.Equal("new@example.org", instance.Properties["Supervisor"].AsBsonDocument["Email"].AsString);
        Assert.Equal("new@example.org", instance.Properties["Supervisor"].AsBsonDocument["UserName"].AsString);
        Assert.Equal("new@example.org", instance.Properties["Reviewer"].AsBsonDocument["Email"].AsString);
        Assert.Equal("new@example.org", instance.Properties["Reviewer"].AsBsonDocument["UserName"].AsString);
        _workflowInstanceRepoMock.Verify(r => r.UpdateFields(instance.Id,
            It.IsAny<UpdateDefinition<WorkflowInstance>>(), _ct), Times.Exactly(2));
    }

    [Fact]
    public async Task Users_UpdateExternalUser_ReturnsNotFound_WhenUserDoesNotExist()
    {
        var controller = BuildControllerWithRoles(["Api"]);

        var result = await controller.UpdateExternalUser("missing-user-id",
            new UpdateUserEmailDto(
                new ExternalUserDto("External User", "new@example.org", null, "missing-user-id"), InstanceId),
            _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
        _userRepoMock.Verify(r => r.Update(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("internal", UserInvitationState.Required)]
    [InlineData("eduid", UserInvitationState.Completed)]
    public async Task Users_UpdateExternalUser_RejectsUsersThatAreNotEligible(
        string providerKey,
        UserInvitationState invitationState)
    {
        var user = new User
        {
            Id = "user-id",
            UserName = "old@example.org",
            DisplayName = "User",
            Email = "old@example.org",
            ProviderKey = providerKey,
            InvitationState = invitationState
        };
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        var controller = BuildControllerWithRoles(["Api"]);

        var result = await controller.UpdateExternalUser(user.Id,
            new UpdateUserEmailDto(new ExternalUserDto("External User", "new@example.org", null, user.Id),
                InstanceId), _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, objectResult.StatusCode);
        var error = Assert.IsType<Error>(objectResult.Value);
        Assert.Equal("UserUpdateNotAllowed", error.ErrorCode);
        _userRepoMock.Verify(r => r.Update(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Users_UpdateExternalUser_RejectsDuplicateEmailFromAnotherUser()
    {
        var user = new User
        {
            Id = ExternalUserId,
            UserName = "old@example.org",
            DisplayName = "External User",
            Email = "old@example.org",
            ProviderKey = "eduid",
            InvitationState = UserInvitationState.Required
        };
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        _externalUserServiceMock
            .Setup(s => s.CreateOrUpdateExternalUser(It.IsAny<string>(), "duplicate@example.org",
                It.IsAny<Organization?>(), It.IsAny<string?>(), _ct))
            .ThrowsAsync(new ExternalUserCreationException(
                ExternalUserCreationFailureReason.UserAlreadyExists, "Email already taken"));
        var controller = BuildControllerWithRoles(["Api"]);

        var result = await controller.UpdateExternalUser(user.Id,
            new UpdateUserEmailDto(new ExternalUserDto("External User", "duplicate@example.org", null, user.Id),
                InstanceId), _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        var error = Assert.IsType<Error>(objectResult.Value);
        Assert.Equal("ManualUserEmailAlreadyExists", error.ErrorCode);
        _userRepoMock.Verify(r => r.Update(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Users_UpdateExternalUser_AllowsSameEmailForSameUser()
    {
        var user = new User
        {
            Id = ExternalUserId,
            UserName = "old@example.org",
            DisplayName = "External User",
            Email = "old@example.org",
            ProviderKey = "eduid",
            InvitationState = UserInvitationState.Required
        };
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        var controller = BuildControllerWithRoles(["Api"]);

        var result = await controller.UpdateExternalUser(user.Id,
            new UpdateUserEmailDto(new ExternalUserDto("External User", "old@example.org", null, user.Id),
                InstanceId), _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<UserDto>(okResult.Value);
        Assert.Equal("old@example.org", dto.Email);
        _externalUserServiceMock.Verify(s => s.CreateOrUpdateExternalUser(
            "External User", "old@example.org", null, user.Id, _ct), Times.Once);
        _userRepoMock.Verify(r => r.Update(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("student@uva.nl", StatusCodes.Status400BadRequest, "ManualUserInternalEmail",
        ExternalUserCreationFailureReason.InternalEmailAddress)]
    [InlineData("not-an-email", StatusCodes.Status400BadRequest, "InvalidEmailAddress",
        ExternalUserCreationFailureReason.InvalidEmailAddress)]
    public async Task Users_UpdateExternalUser_RejectsInvalidTargetEmail(
        string email,
        int expectedStatusCode,
        string expectedErrorCode,
        ExternalUserCreationFailureReason reason)
    {
        var user = new User
        {
            Id = ExternalUserId,
            UserName = "old@example.org",
            DisplayName = "External User",
            Email = "old@example.org",
            ProviderKey = "eduid",
            InvitationState = UserInvitationState.Required
        };
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        _externalUserServiceMock
            .Setup(s => s.CreateOrUpdateExternalUser(It.IsAny<string>(), email,
                It.IsAny<Organization?>(), It.IsAny<string?>(), _ct))
            .ThrowsAsync(new ExternalUserCreationException(reason, "Service error"));

        var controller = BuildControllerWithRoles(["Api"]);

        var result = await controller.UpdateExternalUser(user.Id,
            new UpdateUserEmailDto(new ExternalUserDto("External User", email, null, user.Id), InstanceId), _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(expectedStatusCode, objectResult.StatusCode);
        var error = Assert.IsType<Error>(objectResult.Value);
        Assert.Equal(expectedErrorCode, error.ErrorCode);
        _userRepoMock.Verify(r => r.Update(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Users_UpdateExternalUser_RequiresAnswerEditRights()
    {
        _userRepoMock.Setup(r => r.GetById(ExternalUserId, _ct)).ReturnsAsync(new User
        {
            Id = ExternalUserId,
            UserName = "old@example.org",
            DisplayName = "External User",
            Email = "old@example.org",
            ProviderKey = "eduid",
            InvitationState = UserInvitationState.Required
        });
        var controller = BuildControllerWithRoles(["HasNoRights"]);

        var result = await controller.UpdateExternalUser(ExternalUserId,
            new UpdateUserEmailDto(new ExternalUserDto("External User", "new@example.org", null, ExternalUserId),
                InstanceId), _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    [Fact]
    public async Task Users_UpdateExternalUser_ReturnsUnprocessable_WhenUserIdMismatch()
    {
        var controller = BuildControllerWithRoles(["Api"]);

        var result = await controller.UpdateExternalUser("url-id",
            new UpdateUserEmailDto(new ExternalUserDto("Name", "new@example.org", null, "body-id"), InstanceId),
            _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, objectResult.StatusCode);
        var error = Assert.IsType<Error>(objectResult.Value);
        Assert.Equal("UserIdMismatch", error.ErrorCode);
    }

    [Theory]
    [InlineData("student@uva.nl")]
    [InlineData("student@sub.uva.nl")]
    [InlineData("STUDENT@SUB.UVA.NL")]
    public async Task Users_VerifyEmail_RejectsInternalEmail(string email)
    {
        var controller = BuildControllerWithRoles(["Student"]);

        var result = await controller.VerifyEmail(new VerifyEmailRequest(email), _ct);

        var badRequestResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
        var error = Assert.IsType<Error>(badRequestResult.Value);
        Assert.Equal("ManualUserInternalEmail", error.ErrorCode);
        Assert.Equal("Internal email address", error.Message);
        _userRepoMock.Verify(r => r.GetByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("doctor@amsterdamumc.nl")]
    [InlineData("doctor@dept.amsterdamumc.nl")]
    public async Task Users_VerifyEmail_AllowsAmsterdamUmcEmail(string email)
    {
        var controller = BuildControllerWithRoles(["Student"]);

        var result = await controller.VerifyEmail(new VerifyEmailRequest(email), _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<VerifyEmailResponse>(okResult.Value);
        Assert.Equal(email, response.Email);
        Assert.Equal("Valid", response.Status);
        _userRepoMock.Verify(r => r.GetByEmail(email, _ct), Times.Once);
    }

    [Fact]
    public async Task Users_VerifyEmail_RejectsDuplicateEmail()
    {
        var controller = BuildControllerWithRoles(["Student"]);
        _userRepoMock.Setup(r => r.GetByEmail("Duplicate@Example.Org", _ct))
            .ReturnsAsync(new User { Email = "DUPLICATE@example.org" });

        var result = await controller.VerifyEmail(new VerifyEmailRequest("Duplicate@Example.Org"), _ct);

        var conflictResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflictResult.StatusCode);
        var error = Assert.IsType<Error>(conflictResult.Value);
        Assert.Equal("ManualUserEmailAlreadyExists", error.ErrorCode);
        Assert.Equal("Email already exists", error.Message);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task Users_VerifyEmail_RejectsInvalidEmailFormat(string email)
    {
        var controller = BuildControllerWithRoles(["Student"]);

        var result = await controller.VerifyEmail(new VerifyEmailRequest(email), _ct);

        var badRequestResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
        var error = Assert.IsType<Error>(badRequestResult.Value);
        Assert.Equal("InvalidEmailAddress", error.ErrorCode);
        Assert.Equal("Invalid email address", error.Message);
        _userRepoMock.Verify(r => r.GetByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Users_VerifyEmail_TrimsEmailBeforeValidation()
    {
        var controller = BuildControllerWithRoles(["Student"]);

        var result = await controller.VerifyEmail(new VerifyEmailRequest("  doctor@amsterdamumc.nl  "), _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<VerifyEmailResponse>(okResult.Value);
        Assert.Equal("doctor@amsterdamumc.nl", response.Email);
        _userRepoMock.Verify(r => r.GetByEmail("doctor@amsterdamumc.nl", _ct), Times.Once);
    }

    [Fact]
    public async Task Users_GetLoggedInUser_ReturnsNotFound_WhenNoUserIsAuthenticated()
    {
        _userServiceMock.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        var controller = new UsersController(_userServiceMock.Object,
            _userRepoMock.Object,
            _workflowInstanceRepoMock.Object,
            _rightsService,
            _eduIdUserServiceMock.Object,
            _externalUserServiceMock.Object,
            null!,
            null!,
            _externalUserEmailUpdateService,
            Mock.Of<ILogger<UsersController>>());

        var result = await controller.GetLoggedInUser(_ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task Users_Find_PreservesExternalFlag()
    {
        _userServiceMock.Setup(s => s.FindUsers("external", true, _ct))
            .ReturnsAsync([
                new UserSearchResult("external-123",
                    "External User",
                    "external@example.org",
                    "eduid",
                    "eduid")
            ]);
        var controller = BuildControllerWithRoles(["Student"]);

        var result = await controller.Find("external", true, _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var user = Assert.Single(Assert.IsAssignableFrom<IEnumerable<UserSearchResultDto>>(okResult.Value));
        Assert.True(user.IsExternal);
    }

    [Fact]
    public async Task Users_Find_PassesThroughOrganizationFromSearchSource()
    {
        var organization = Organization.Create("FNWI");
        // The organisation is now sourced upstream (the DataNose search source); the controller
        // must surface whatever the search returns without modifying it.
        _userServiceMock.Setup(s => s.FindUsers("internal", true, _ct))
            .ReturnsAsync([
                new UserSearchResult("internal-123",
                    "Internal User",
                    "internal@uva.nl",
                    UserSearchSources.Repository,
                    Organization: organization)
            ]);
        var controller = BuildControllerWithRoles(["Student"]);

        var result = await controller.Find("internal", true, _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var user = Assert.Single(Assert.IsAssignableFrom<IEnumerable<UserSearchResultDto>>(okResult.Value));
        Assert.NotNull(user.Organization);
        Assert.Equal(organization.Id, user.Organization!.Id);
        Assert.Equal(organization.Name, user.Organization.Name);
        Assert.False(user.IsExternal);
    }

    [Fact]
    public async Task Users_Find_DoesNotInventOrganizationWhenSearchReturnsNone()
    {
        _userServiceMock.Setup(s => s.FindUsers("external", true, _ct))
            .ReturnsAsync([
                new UserSearchResult("external-123",
                    "External User",
                    "external@example.org",
                    UserSearchSources.Repository,
                    "eduid")
            ]);
        var controller = BuildControllerWithRoles(["Student"]);

        var result = await controller.Find("external", true, _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var user = Assert.Single(Assert.IsAssignableFrom<IEnumerable<UserSearchResultDto>>(okResult.Value));
        Assert.Null(user.Organization);
        Assert.True(user.IsExternal);
    }

    [Fact]
    public async Task Users_Find_DefaultsToIncludingExternalUsers()
    {
        _userServiceMock.Setup(s => s.FindUsers("external", true, _ct))
            .ReturnsAsync([]);
        var controller = BuildControllerWithRoles(["Student"]);

        await controller.Find("external", ct: _ct);

        _userServiceMock.Verify(s => s.FindUsers("external", true, _ct), Times.Once);
    }

    [Fact]
    public async Task Users_Find_CanExcludeExternalUsers()
    {
        _userServiceMock.Setup(s => s.FindUsers("external", false, _ct))
            .ReturnsAsync([
                new UserSearchResult("internal-123",
                    "Internal User",
                    "internal@example.org",
                    UserSearchSources.Repository)
            ]);
        var controller = BuildControllerWithRoles(["Student"]);

        var result = await controller.Find("external", false, _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var user = Assert.Single(Assert.IsAssignableFrom<IEnumerable<UserSearchResultDto>>(okResult.Value));
        Assert.False(user.IsExternal);
        _userServiceMock.Verify(s => s.FindUsers("external", false, _ct), Times.Once);
    }

    private UsersController BuildControllerWithRoles(
        string[] roles)
    {
        MockCurrentUser(roles);
        _eduIdUserServiceMock.Setup(s => s.IsInternalEmailAddress(It.IsAny<string>()))
            .Returns((string email) => IsConfiguredInternalEmail(email));
        _workflowInstanceRepoMock.Setup(r => r.GetById(InstanceId, _ct))
            .ReturnsAsync(new WorkflowInstanceBuilder()
                .With(workflowDefinition: "Project", currentStep: "Start", id: InstanceId)
                .WithProperties(("Supervisor", _ => new BsonDocument
                {
                    { "_id", ObjectId.Parse(ExternalUserId) },
                    { "UserName", "old@example.org" },
                    { "DisplayName", "External User" },
                    { "Email", "old@example.org" }
                }))
                .WithEvent("Start", DateTime.UtcNow)
                .Build());
        _workflowInstanceRepoMock.Setup(r => r.UpdateFields(InstanceId,
                It.IsAny<UpdateDefinition<WorkflowInstance>>(), _ct))
            .Returns(Task.CompletedTask);
        _userServiceMock.Setup(s => s.GetUser(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string userName, CancellationToken _) => new User
            {
                Id = ExternalUserId,
                UserName = userName,
                DisplayName = "External User",
                Email = userName,
                ProviderKey = "eduid",
                InvitationState = UserInvitationState.Required
            });

        return new UsersController(_userServiceMock.Object,
            _userRepoMock.Object,
            _workflowInstanceRepoMock.Object,
            _rightsService,
            _eduIdUserServiceMock.Object,
            _externalUserServiceMock.Object,
            null!,
            null!,
            _externalUserEmailUpdateService,
            Mock.Of<ILogger<UsersController>>());
    }

    private static bool IsConfiguredInternalEmail(string email)
    {
        var domain = email.Trim().Split('@')[^1];
        return new[] { "uva.nl", "auc.nl" }.Any(internalDomain =>
            domain.Equals(internalDomain, StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith($".{internalDomain}", StringComparison.OrdinalIgnoreCase));
    }
}