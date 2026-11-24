using MongoDB.Bson;
using Moq;
using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.Screens.Dtos;
using UvA.Workflow.Tests.Controllers.Helpers;

namespace UvA.Workflow.Tests;

public class ScreenDataServiceRelatedUsersTests : ControllerTestsBase
{
    private readonly ScreenDataService _service;
    private readonly Screen _screen;
    private readonly WorkflowDefinition _definition;

    public ScreenDataServiceRelatedUsersTests()
    {
        MockCurrentUser("Student");
        MockEmptyRelatedInstanceLookups();

        _definition = _modelService.WorkflowDefinitions["Project"];
        _screen = new Screen
        {
            Name = "RelatedUsersTests",
            Columns =
            [
                new Column
                {
                    Type = ColumnType.RelatedUsers,
                    Title = "Staff"
                }
            ]
        };
        _definition.Screens.Add(_screen);

        _service = new ScreenDataService(
            _modelService,
            _instanceService,
            _workflowInstanceRepoMock.Object,
            new InstanceAuthorizationFilterService(
                _rightsService,
                _modelService,
                _userServiceMock.Object,
                _workflowInstanceRepoMock.Object),
            _rightsService);
    }

    [Fact]
    public async Task GetScreenData_SingleAndArrayUsers_MergesRolesByDisplayName()
    {
        var supervisorTitle = new BilingualString("Supervisor", "Begeleider");
        var practicalTitle = new BilingualString(
            "Practical supervisor", "Praktijkbegeleider");

        ConfigureGroups(DefaultGroup(
            new RelatedUser { Property = "Supervisor", Text = supervisorTitle },
            new RelatedUser { Property = "PracticalSupervisor", Text = practicalTitle }));

        var result = await Load(new Dictionary<string, BsonValue>
        {
            ["Supervisor"] = User("Alice", "First organisation"),
            ["PracticalSupervisor"] = new BsonArray
            {
                User("Alice", "Second organisation"),
                BsonNull.Value,
                User("Bob")
            }
        });

        var column = Assert.Single(result.Columns);
        Assert.Equal(ColumnType.RelatedUsers, column.ColumnType);
        Assert.Equal(DataType.User, column.DataType);

        Assert.Collection(Users(result),
            user =>
            {
                Assert.Equal("Alice", user.DisplayName);
                Assert.Equal("First organisation", user.Organisation);
                Assert.Equal(
                    new[] { supervisorTitle, practicalTitle },
                    user.Roles);
            },
            user =>
            {
                Assert.Equal("Bob", user.DisplayName);
                Assert.Null(user.Organisation);
                Assert.Equal(practicalTitle, Assert.Single(user.Roles));
            });
    }

    [Fact]
    public async Task GetScreenData_OnlyIncludesAndProjectsDefaultGroupUsers()
    {
        ConfigureGroups(
            DefaultGroup(Role("Supervisor")),
            new RelatedUserGroup
            {
                Name = "support",
                Users = [Role("Reviewer")]
            });

        var result = await Load(new Dictionary<string, BsonValue>
        {
            ["Supervisor"] = User("Alice"),
            ["Reviewer"] = User("Bob")
        });

        Assert.Equal("Alice", Assert.Single(Users(result)).DisplayName);

        _workflowInstanceRepoMock.Verify(repository => repository.GetAllByType(
            "Project",
            It.Is<Dictionary<string, string>>(projection =>
                projection["Supervisor"] == "$Properties.Supervisor" &&
                !projection.ContainsKey("Reviewer")),
            It.IsAny<BsonDocument?>(),
            _ct), Times.Once);
    }

    [Fact]
    public async Task GetScreenData_MissingNullEmptyAndNonUserValues_ReturnsEmptyArray()
    {
        ConfigureGroups(DefaultGroup(
            Role("Supervisor"),
            Role("Reviewer"),
            Role("PracticalSupervisor"),
            Role("Title")));

        var result = await Load(new Dictionary<string, BsonValue>
        {
            ["Reviewer"] = BsonNull.Value,
            ["PracticalSupervisor"] = new BsonArray(),
            ["Title"] = "Not a user"
        });

        Assert.Empty(Users(result));
    }

    [Fact]
    public async Task GetScreenData_ReferencedUsers_ProjectsAndEnrichesReference()
    {
        ConfigureGroups(DefaultGroup(Role("Course.Coordinator")));
        var courseId = ObjectId.GenerateNewId();

        _workflowInstanceRepoMock.Setup(repository => repository.GetAllById(
                It.Is<string[]>(ids =>
                    ids.Length == 1 && ids[0] == courseId.ToString()),
                It.Is<Dictionary<string, string>>(projection =>
                    projection.Count == 1 &&
                    projection["Coordinator"] == "$Properties.Coordinator"),
                _ct))
            .ReturnsAsync(
            [
                new Dictionary<string, BsonValue>
                {
                    ["_id"] = courseId,
                    ["Coordinator"] = new BsonArray
                    {
                        User("Alice", "University")
                    }
                }
            ]);

        var result = await Load(new Dictionary<string, BsonValue>
        {
            ["Course"] = courseId
        });

        var user = Assert.Single(Users(result));
        Assert.Equal("Alice", user.DisplayName);
        Assert.Equal("University", user.Organisation);
        Assert.Equal("Course.Coordinator", Assert.Single(user.Roles).En);

        _workflowInstanceRepoMock.Verify(repository => repository.GetAllByType(
            "Project",
            It.Is<Dictionary<string, string>>(projection =>
                projection["Course"] == "$Properties.Course" &&
                !projection.ContainsKey("Course.Coordinator")),
            It.IsAny<BsonDocument?>(),
            _ct), Times.Once);

        _workflowInstanceRepoMock.Verify(repository => repository.GetAllById(
            It.IsAny<string[]>(),
            It.IsAny<Dictionary<string, string>>(),
            _ct), Times.Once);
    }

    [Fact]
    public async Task GetScreenData_WithoutRelatedUsersColumn_DoesNotLoadRelatedUsers()
    {
        ConfigureGroups(DefaultGroup(Role("Course.Coordinator")));
        _screen.Columns =
        [
            new Column { Property = "Title", Title = "Title" }
        ];

        var result = await Load(new Dictionary<string, BsonValue>
        {
            ["Title"] = "Project title",
            ["Course"] = ObjectId.GenerateNewId()
        });

        var column = Assert.Single(result.Columns);
        Assert.Equal("Project title", Assert.Single(result.Rows).Values[column.Id]);

        _workflowInstanceRepoMock.Verify(repository => repository.GetAllByType(
            "Project",
            It.Is<Dictionary<string, string>>(projection =>
                !projection.ContainsKey("Course")),
            It.IsAny<BsonDocument?>(),
            _ct), Times.Once);

        _workflowInstanceRepoMock.Verify(repository => repository.GetAllById(
            It.IsAny<string[]>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private void ConfigureGroups(params RelatedUserGroup[] groups)
    {
        _definition.InfoCards =
        [
            new InfoCard
            {
                Name = "RelatedUsers",
                Type = InfoCardType.RelatedUsers,
                Groups = groups
            }
        ];
    }

    private async Task<ScreenDataDto> Load(Dictionary<string, BsonValue> row)
    {
        row["_id"] = ObjectId.GenerateNewId();
        row["CurrentStep"] = "Start";

        _workflowInstanceRepoMock.Setup(repository => repository.GetAllByType(
                "Project",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<BsonDocument?>(),
                _ct))
            .ReturnsAsync([row]);

        return await _service.GetScreenData(_screen.Name, "Project", _ct);
    }

    private static ScreenRelatedUserDto[] Users(ScreenDataDto result) =>
        Assert.IsType<ScreenRelatedUserDto[]>(
            Assert.Single(result.Rows).Values[Assert.Single(result.Columns).Id]);

    private static RelatedUserGroup DefaultGroup(params RelatedUser[] users) =>
        new() { Name = "default", Users = users };

    private static RelatedUser Role(string property) =>
        new() { Property = property, Text = property };

    private static BsonDocument User(string displayName, string? organisation = null) =>
        new()
        {
            ["_id"] = ObjectId.GenerateNewId(),
            ["UserName"] = displayName.ToLowerInvariant(),
            ["DisplayName"] = displayName,
            ["Email"] = $"{displayName.ToLowerInvariant()}@example.test",
            ["Organization"] = organisation == null
                ? BsonNull.Value
                : new BsonDocument("Name", organisation)
        };
}