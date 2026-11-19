using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using UvA.Workflow.Api.Personal.Dtos;
using UvA.Workflow.Api.Users.Dtos;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public partial class PersonalControllerTests
{
    [Fact]
    public async Task SearchAndGetUserInstances_RequireOverviewAccess()
    {
        MockCurrentUser();
        var directAssignment = new WorkflowInstanceBuilder().With("Project", "Start")
            .WithProperties(("Supervisor", property => property.Value(
                InstanceUser.FromUser(UnitTestsHelpers.AdminUser).ToBsonDocument())))
            .Build();
        Assert.True(await _rightsService.Can(directAssignment, RoleAction.View));

        var search = await _controller.Search("example", _ct);
        var detail = await _controller.GetUserInstances(ObjectId.GenerateNewId().ToString(), _ct);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(search.Result).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(detail.Result).StatusCode);
        _userRepoMock.Verify(repository => repository.SearchByPrefix(It.IsAny<string>(), _ct), Times.Never);
        _userRepoMock.Verify(repository => repository.GetById(It.IsAny<string>(), _ct), Times.Never);
    }

    [Fact]
    public async Task Search_ReturnsOnlyPeopleLinkedToVisibleInstances()
    {
        MockCurrentUser("Coordinator");
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

        var result = await _controller.Search("  example ", _ct);

        var response = Assert.IsType<PeopleSearchDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(UserDto.Create(visible), Assert.Single(response.People));
        Assert.False(response.HasMore);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(51)]
    public async Task Search_CountsOnlyVisiblePeopleTowardsResultLimit(int visibleCount)
    {
        MockCurrentUser("Supervisor");
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

        var result = await _controller.Search("example", _ct);

        var response = Assert.IsType<PeopleSearchDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(visible.Take(50).Select(user => UserDto.Create(user)), response.People);
        Assert.Equal(visibleCount > 50, response.HasMore);
    }

    [Fact]
    public async Task Search_SupportsCoordinatorsAssignedThroughCourses()
    {
        MockCurrentUser();
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

        var result = await _controller.Search("supervisor", _ct);

        var response = Assert.IsType<PeopleSearchDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(UserDto.Create(person), Assert.Single(response.People));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetUserInstances_OnlyReturnsAuthorizedProjectsThatInheritThePersonsRole(bool transitive)
    {
        MockCurrentUser("Coordinator");
        var (person, _, visibleProject) = MockInheritedAssignments(transitive);
        _userRepoMock.Setup(repository => repository.GetById(person.Id, _ct)).ReturnsAsync(person);

        var result = await _controller.GetUserInstances(person.Id, _ct);

        var response = Assert.IsType<PersonalInstancesDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var instance = Assert.Single(response.Instances);
        Assert.Equal(visibleProject.Id, instance.Id);
        Assert.Equal([transitive ? "LocalAdmin" : "Coordinator"], instance.Roles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Search_FindsPeopleWhoseOnlyVisibleAssignmentIsInherited(bool transitive)
    {
        MockCurrentUser("Coordinator");
        var (visiblePerson, hiddenPerson, _) = MockInheritedAssignments(transitive);
        _userRepoMock.Setup(repository => repository.SearchByPrefix("example", _ct))
            .Returns(new[] { hiddenPerson, visiblePerson }.ToAsyncEnumerable());
        var result = await _controller.Search("example", _ct);

        var response = Assert.IsType<PeopleSearchDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(UserDto.Create(visiblePerson), Assert.Single(response.People));
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
}