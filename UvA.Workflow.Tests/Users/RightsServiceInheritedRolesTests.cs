using MongoDB.Bson;
using Moq;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Users;

public class RightsServiceInheritedRolesTests
{
    private const string CurrentUserId = "000000000000000000000001";
    private const string OtherUserId = "000000000000000000000002";

    private readonly ModelService _model = new(UnitTestsHelpers.CreateModelParser());
    private readonly Mock<IUserService> _users = new();
    private readonly Mock<IWorkflowInstanceRepository> _repository = new(MockBehavior.Strict);
    private readonly List<WorkflowInstance> _referencedInstances = [];
    private readonly List<string[]> _requests = [];
    private readonly RightsService _sut;

    public RightsServiceInheritedRolesTests()
    {
        _users.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = CurrentUserId, UserName = "current-user" });
        _users.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repository.Setup(r => r.GetByIds(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) =>
            {
                var requestedIds = ids.ToArray();
                _requests.Add(requestedIds);
                return _referencedInstances.Where(i => requestedIds.Contains(i.Id)).ToArray();
            });
        _sut = new RightsService(_model, _users.Object, _repository.Object);
    }

    [Fact]
    public async Task GetViewerRoles_CombinesInheritedDocumentAndArrayAssignmentsWithDirectAndGlobalRoles()
    {
        _users.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["SystemAdmin"]);
        var course = Instance("Course",
            ("Coordinator", new BsonArray { UserDocument(OtherUserId), UserDocument() }),
            ("Impersonator", UserDocument()));
        var project = CreateProjectWithCourse(course);
        project.SetProperty(UserDocument(), "Student");
        _referencedInstances.Add(course);

        var roles = await _sut.GetViewerRoles(project);

        AssertRoles(roles, "Registered", "SystemAdmin", "Student", "Coordinator", "Impersonator");
    }

    [Fact]
    public async Task GetViewerRoles_InheritsTransitivelyThroughInstancesWithoutOwnInheritableRoles()
    {
        var faculty = Instance("Faculty", ("LocalAdmin", new BsonArray { UserDocument() }));
        var programme = Instance("Programme", ("Faculty", faculty.Id));
        var course = Instance("Course",
            ("Programme", programme.Id),
            ("Coordinator", new BsonArray { UserDocument() }));
        _referencedInstances.AddRange([course, programme, faculty]);

        var roles = await _sut.GetViewerRoles(CreateProjectWithCourse(course));

        AssertRoles(roles, "Registered", "Coordinator", "LocalAdmin");
    }

    [Fact]
    public async Task GetViewerRoles_DoesNotInheritRolesUnlessExplicitlyEnabled()
    {
        var properties = _model.WorkflowDefinitions["Course"].Properties.OfType<UserPropertyDefinition>();
        properties.Single(p => p.Name == "Coordinator").IsInheritableRole = false;
        properties.Single(p => p.Name == "Impersonator").IsInheritableRole = null;
        var course = Instance("Course",
            ("Coordinator", new BsonArray { UserDocument() }),
            ("Impersonator", UserDocument()));
        _referencedInstances.Add(course);

        var roles = await _sut.GetViewerRoles(CreateProjectWithCourse(course));

        AssertRoles(roles, "Registered");
    }

    [Fact]
    public async Task GetViewerRoles_DoesNotInheritAssignmentsForAnotherUser()
    {
        var course = Instance("Course",
            ("Coordinator", new BsonArray { UserDocument(OtherUserId) }),
            ("Impersonator", UserDocument(OtherUserId)));
        _referencedInstances.Add(course);

        var roles = await _sut.GetViewerRoles(CreateProjectWithCourse(course));

        AssertRoles(roles, "Registered");
    }

    [Fact]
    public async Task GetViewerRoles_ResolvesReferenceArraysWithDuplicatesAndMissingInstances()
    {
        var first = Instance("Course", ("Coordinator", new BsonArray { UserDocument() }));
        var second = Instance("Course", ("Impersonator", new BsonArray { UserDocument() }));
        _referencedInstances.AddRange([second, first]);
        var project = Instance("Project",
            ("Course", new BsonArray { first.Id, "missing-course", second.Id, first.Id }));

        var roles = await _sut.GetViewerRoles(project);

        AssertRoles(roles, "Registered", "Coordinator", "Impersonator");
        Assert.Equal(new[] { first.Id, "missing-course", second.Id }, Assert.Single(_requests));
    }

    [Fact]
    public async Task GetViewerRoles_DoesNotTrustInstanceWithWrongWorkflowDefinitionOrTraverseIt()
    {
        var faculty = Instance("Faculty", ("LocalAdmin", new BsonArray { UserDocument() }));
        var wrongType = Instance("Programme",
            ("Coordinator", new BsonArray { UserDocument() }),
            ("Faculty", faculty.Id));
        _referencedInstances.AddRange([wrongType, faculty]);
        var project = Instance("Project", ("Course", wrongType.Id));

        AssertRoles(await _sut.GetViewerRoles(project), "Registered");
        Assert.Equal(new[] { wrongType.Id }, Assert.Single(_requests));
    }

    [Fact]
    public async Task GetViewerRoles_TerminatesReferenceCycleWithoutLosingRoles()
    {
        var course = Instance("Course", ("Coordinator", new BsonArray { UserDocument() }));
        var project = CreateProjectWithCourse(course);
        // The fixtures are acyclic; add a back reference to exercise cycle protection.
        _model.WorkflowDefinitions["Course"].Properties.Add(new ReferencePropertyDefinition
        {
            Name = "BackReference",
            Type = "Project",
            WorkflowDefinition = _model.WorkflowDefinitions["Project"]
        });
        course.SetProperty(project.Id, "BackReference");
        _referencedInstances.AddRange([project, course]);

        var roles = await _sut.GetViewerRoles(project);

        AssertRoles(roles, "Registered", "Coordinator");
        Assert.Equal(2, _requests.Count);
    }

    [Fact]
    public async Task GetAllowedActions_UsesConsumingWorkflowPermissionsForInheritedRole()
    {
        var course = Instance("Course", ("Coordinator", new BsonArray { UserDocument() }));
        var project = CreateProjectWithCourse(course);
        _referencedInstances.Add(course);
        var consumingRole = _model.WorkflowDefinitions["Project"].Roles.Single(r => r.Name == "Coordinator");
        var sourceRole = _model.WorkflowDefinitions["Course"].Roles.Single(r => r.Name == "Coordinator");
        Assert.DoesNotContain(sourceRole.Actions, a => a.Type == RoleAction.Edit);
        var expected = consumingRole.Actions.Where(a => a.Type == RoleAction.Edit).ToArray();
        Assert.NotEmpty(expected);

        var actions = await _sut.GetAllowedActions(project, RoleAction.Edit);

        Assert.Equal(expected, actions);
    }

    [Fact]
    public async Task GetViewerRoles_WithoutReferencesKeepsDirectRolesAndDoesNotQueryRepository()
    {
        var project = Instance("Project", ("Student", UserDocument()));

        var roles = await _sut.GetViewerRoles(project);

        AssertRoles(roles, "Registered", "Student");
        _repository.VerifyNoOtherCalls();
    }

    private static BsonDocument UserDocument(string id = CurrentUserId)
        => new() { ["_id"] = ObjectId.Parse(id), ["UserName"] = "test-user" };

    private static WorkflowInstance CreateProjectWithCourse(WorkflowInstance course)
        => Instance("Project", ("Course", course.Id));

    private static WorkflowInstance Instance(string definition, params (string Name, BsonValue Value)[] properties)
        => new()
        {
            Id = ObjectId.GenerateNewId().ToString(),
            WorkflowDefinition = definition,
            CurrentStep = definition == "Project" ? "Upload" : null,
            Properties = properties.ToDictionary(p => p.Name, p => p.Value),
            Events = []
        };

    private static void AssertRoles(IEnumerable<string> actual, params string[] expected)
        => Assert.Equal(expected.OrderBy(r => r), actual.OrderBy(r => r));
}