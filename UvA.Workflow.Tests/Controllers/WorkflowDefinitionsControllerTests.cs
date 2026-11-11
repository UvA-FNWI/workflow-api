using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.WorkflowDefinitions;
using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;

namespace UvA.Workflow.Tests.Controllers;

public class WorkflowDefinitionsControllerTests : ControllerTestsBase
{
    private WorkflowDefinitionsController BuildController(params string[] roles)
    {
        MockCurrentUser(roles);
        var authorizationFilterService = new InstanceAuthorizationFilterService(
            _rightsService, _modelService, _userServiceMock.Object, _workflowInstanceRepoMock.Object);
        return new WorkflowDefinitionsController(_modelService, _rightsService, authorizationFilterService);
    }

    private static List<WorkflowDefinitionDto> GetDtos(ActionResult<IEnumerable<WorkflowDefinitionDto>> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsAssignableFrom<IEnumerable<WorkflowDefinitionDto>>(ok.Value).ToList();
    }

    [Fact]
    public async Task GetAll_Default_OnlyReturnsDefinitionsWithScreens()
    {
        var controller = BuildController("Coordinator");

        var dtos = GetDtos(await controller.GetAll());

        Assert.NotEmpty(dtos);
        Assert.All(dtos, d => Assert.NotEmpty(d.Screens));
        // "Assessment" has no screens, so it must be excluded by default
        Assert.DoesNotContain(dtos, d => d.Name == "Assessment");
    }

    [Fact]
    public async Task GetAll_IncludeAll_IncludesDefinitionsWithoutScreens()
    {
        var controller = BuildController("Coordinator");

        var defaultDtos = GetDtos(await controller.GetAll());
        var allDtos = GetDtos(await controller.GetAll(includeAll: true));

        Assert.True(allDtos.Count > defaultDtos.Count);
        Assert.Contains(allDtos, d => d.Screens.Length == 0);
        Assert.True(Assert.Single(allDtos, d => d.Name == "Assessment").IsPropertyOnly);
        Assert.False(Assert.Single(allDtos, d => d.Name == "Project").IsPropertyOnly);
    }

    [Fact]
    public async Task GetAll_SetsCanCreateInstance_ForDefinitionsTheUserCanCreate()
    {
        // The "Project" definition grants CreateInstance to the always-present "Registered" role.
        var controller = BuildController();

        var dtos = GetDtos(await controller.GetAll(includeAll: true));

        var project = Assert.Single(dtos, d => d.Name == "Project");
        Assert.True(project.CanCreateInstance);

        // "Context" is only creatable by the "Api" role, which this user does not have.
        var context = Assert.Single(dtos, d => d.Name == "Context");
        Assert.False(context.CanCreateInstance);
    }

    [Fact]
    public async Task GetAll_CanCreateInstance_RespectsRole()
    {
        // The "Api" role grants CreateInstance for "Context".
        var controller = BuildController("Api");

        var dtos = GetDtos(await controller.GetAll(includeAll: true));

        var context = Assert.Single(dtos, d => d.Name == "Context");
        Assert.True(context.CanCreateInstance);
    }

    [Fact]
    public async Task GetAccessible_ReturnsOnlyDefinitionsWithInheritedAccess()
    {
        var controller = BuildController();
        MockNoVisibleInstances();
        var courseId = ObjectId.GenerateNewId();
        MockVisibleInstancesFor("Course",
            new BsonDocument("Properties.Coordinator._id", ObjectId.Parse(UnitTestsHelpers.AdminUser.Id)),
            courseId);
        MockVisibleInstancesFor("Project-PP",
            new BsonDocument("Properties.Course", new BsonDocument("$in", new BsonArray { courseId.ToString() })),
            ObjectId.GenerateNewId());

        var dtos = GetDtos(await controller.GetAccessible(_ct));

        Assert.All(dtos, d => Assert.NotEmpty(d.Screens));
        Assert.DoesNotContain(dtos, d => d.Name == "Project-RMSS");
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Supervisor")]
    public async Task GetAccessible_ExcludesDirectRoles_WithoutRemovingInstanceAccess(string role)
    {
        var controller = BuildController();
        MockNoVisibleInstances();
        var directFilter = new BsonDocument($"Properties.{role}._id",
            ObjectId.Parse(UnitTestsHelpers.AdminUser.Id));
        _workflowInstanceRepoMock.Setup(r => r.GetAllByType(
                "Project",
                It.IsAny<Dictionary<string, string>>(),
                It.Is<BsonDocument?>(filter => filter != null && filter.ToJson().Contains($"Properties.{role}._id")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Dictionary<string, BsonValue> { ["_id"] = ObjectId.GenerateNewId() }]);

        var dtos = GetDtos(await controller.GetAccessible(_ct));

        Assert.Empty(dtos);
        var authorization = new InstanceAuthorizationFilterService(
            _rightsService, _modelService, _userServiceMock.Object, _workflowInstanceRepoMock.Object);
        var instanceFilter = await authorization.BuildAuthorizationFilter("Project", RoleAction.View, _ct);
        Assert.Contains(directFilter.ToJson(), instanceFilter!.ToJson());
    }

    [Theory]
    [InlineData("Coordinator")]
    [InlineData("SystemAdmin")]
    public async Task GetAccessible_IncludesGlobalViewRoles_WithoutInstances(string role)
    {
        var controller = BuildController(role);
        MockNoVisibleInstances();

        var dtos = GetDtos(await controller.GetAccessible(_ct));

        Assert.Contains(dtos, d => d.Name == "Project");
    }

    [Fact]
    public async Task GetAccessible_ReturnsEmpty_WithoutGlobalOrInheritedAccess()
    {
        var controller = BuildController();
        MockNoVisibleInstances();

        var dtos = GetDtos(await controller.GetAccessible(_ct));

        Assert.Empty(dtos);
    }

    [Fact]
    public async Task GetAccessible_ReturnsEmpty_ForUnauthenticatedUser()
    {
        var controller = BuildController();
        MockNoVisibleInstances();
        _userServiceMock.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync((UvA.Workflow.Users.User?)null);

        var dtos = GetDtos(await controller.GetAccessible(_ct));

        Assert.Empty(dtos);
    }

    [Fact]
    public async Task GetAccessible_ReturnsGroupedChildScreens()
    {
        var controller = BuildController("SystemAdmin");
        MockNoVisibleInstances();
        var courseId = ObjectId.GenerateNewId();
        var programmeId = ObjectId.GenerateNewId();
        var facultyId = ObjectId.GenerateNewId();
        MockVisibleInstancesFor("Faculty",
            new BsonDocument("Properties.LocalAdmin._id", ObjectId.Parse(UnitTestsHelpers.AdminUser.Id)),
            facultyId);
        MockVisibleInstancesFor("Programme",
            new BsonDocument("Properties.Faculty", facultyId),
            programmeId);
        MockVisibleInstancesFor("Course",
            new BsonDocument("Properties.Programme", programmeId),
            courseId);

        MockVisibleInstancesFor("Project",
            new BsonDocument("Properties.Course", new BsonDocument("$in", new BsonArray { courseId.ToString() })),
            ObjectId.GenerateNewId());

        var dtos = GetDtos(await controller.GetAccessible(_ct));

        Assert.Contains(dtos, d => d.Name == "Context" && d.Screens.Length == 1 && d.Screens[0] == "Default");
        Assert.Contains(dtos, d => d.Name == "Project");
        Assert.DoesNotContain(dtos, d =>
            d.Name is "Course" or "Programme" or "Faculty" or "Specialisation");
    }

    [Fact]
    public async Task BuildAuthorizationFilter_FollowsLongReferenceChainBetweenCourses()
    {
        MockCurrentUser();

        var courseDefinition = _modelService.WorkflowDefinitions["Course"];
        courseDefinition.Properties.Add(new ReferencePropertyDefinition
        {
            Name = "RelatedCourse",
            Type = "Course",
            WorkflowDefinition = courseDefinition
        });

        var userId = ObjectId.Parse(UnitTestsHelpers.AdminUser.Id);
        var courseA = ObjectId.GenerateNewId();
        var courseB = ObjectId.GenerateNewId();
        var courseC = ObjectId.GenerateNewId();
        var courseD = ObjectId.GenerateNewId();

        // Project -> Course A -> Course B -> Course C -> Course D; the user only coordinates Course D.
        var relatedCourses = new Dictionary<ObjectId, ObjectId?>
        {
            [courseA] = courseB,
            [courseB] = courseC,
            [courseC] = courseD,
            [courseD] = null
        };

        bool Matches(ObjectId courseId, BsonDocument filter) => filter.Elements.All(element => element.Name switch
        {
            "$or" => element.Value.AsBsonArray.Any(f => Matches(courseId, f.AsBsonDocument)),
            "Properties.Coordinator._id" => courseId == courseD && element.Value.AsObjectId == userId,
            "Properties.RelatedCourse" => relatedCourses[courseId] is { } related &&
                                          element.Value["$in"].AsBsonArray.Contains(related.ToString()),
            _ => false
        });

        _workflowInstanceRepoMock.Setup(r => r.GetAllByType(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<BsonDocument?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string definition, Dictionary<string, string> _, BsonDocument? filter,
                    CancellationToken _) =>
                definition != "Course" || filter == null
                    ? []
                    : relatedCourses.Keys
                        .Where(id => Matches(id, filter))
                        .Select(id => new Dictionary<string, BsonValue> { ["_id"] = id })
                        .ToList());

        var authorization = new InstanceAuthorizationFilterService(
            _rightsService, _modelService, _userServiceMock.Object, _workflowInstanceRepoMock.Object);

        var filter = await authorization.BuildAuthorizationFilter("Project", RoleAction.View, _ct);

        Assert.NotNull(filter);
        var json = filter.ToJson();
        Assert.All(new[] { courseA, courseB, courseC, courseD }, id => Assert.Contains(id.ToString(), json));
    }

    [Fact]
    public async Task BuildAuthorizationFilter_FollowsCourseProgrammeCourseSpecialisationProgrammeChain()
    {
        var chain = SetUpCourseProgrammeChain(sameCourses: false, sameProgrammes: false);

        var json = await BuildProjectViewFilterJson();

        Assert.Contains(chain.Course1.ToString(), json);
        Assert.Contains(chain.Course2.ToString(), json);
        Assert.DoesNotContain(chain.UnrelatedCourse.ToString(), json);
    }

    [Fact]
    public async Task BuildAuthorizationFilter_TerminatesAndKeepsAccess_WhenChainLoopsBackToSameCourse()
    {
        // Course 1 -> Programme 1 -> Course 1
        var chain = SetUpCourseProgrammeChain(sameCourses: true, sameProgrammes: false);

        var json = await BuildProjectViewFilterJson();

        Assert.Contains(chain.Course1.ToString(), json);
        Assert.DoesNotContain(chain.UnrelatedCourse.ToString(), json);
    }

    [Fact]
    public async Task BuildAuthorizationFilter_TerminatesAndKeepsAccess_WhenChainLoopsBackToSameProgramme()
    {
        // Programme 1 -> Course 2 -> Specialisation -> Programme 1
        var chain = SetUpCourseProgrammeChain(sameCourses: false, sameProgrammes: true);

        var json = await BuildProjectViewFilterJson();

        Assert.Contains(chain.Course1.ToString(), json);
        Assert.Contains(chain.Course2.ToString(), json);
        Assert.DoesNotContain(chain.UnrelatedCourse.ToString(), json);
    }

    private record CourseProgrammeChain(ObjectId Course1, ObjectId Course2, ObjectId UnrelatedCourse);

    /// <summary>
    /// Builds Course 1 -> Programme 1 -> Course 2 -> Specialisation -> Programme 2 -> Faculty,
    /// where the current user is LocalAdmin of the Faculty. Optionally collapses the two courses
    /// or the two programmes into one instance to create a reference cycle.
    /// </summary>
    private CourseProgrammeChain SetUpCourseProgrammeChain(bool sameCourses, bool sameProgrammes)
    {
        MockCurrentUser();

        // Course -> Programme already exists in the fixtures; add the remaining links of the chain.
        AddReferenceProperty("Programme", "Course", "Course");
        AddReferenceProperty("Course", "Specialisation", "Specialisation");
        AddReferenceProperty("Specialisation", "Programme", "Programme");

        var userId = ObjectId.Parse(UnitTestsHelpers.AdminUser.Id);
        var course1 = ObjectId.GenerateNewId();
        var course2 = sameCourses ? course1 : ObjectId.GenerateNewId();
        var programme1 = ObjectId.GenerateNewId();
        var programme2 = sameProgrammes ? programme1 : ObjectId.GenerateNewId();
        var specialisation = ObjectId.GenerateNewId();
        var faculty = ObjectId.GenerateNewId();
        var unrelatedCourse = ObjectId.GenerateNewId();
        var unrelatedProgramme = ObjectId.GenerateNewId();

        var definitions = new Dictionary<ObjectId, string>
        {
            [course1] = "Course",
            [course2] = "Course",
            [programme1] = "Programme",
            [programme2] = "Programme",
            [specialisation] = "Specialisation",
            [faculty] = "Faculty",
            [unrelatedCourse] = "Course",
            [unrelatedProgramme] = "Programme"
        };

        var references = new (ObjectId From, string Property, ObjectId To)[]
        {
            (course1, "Programme", programme1),
            (programme1, "Course", course2),
            (course2, "Specialisation", specialisation),
            (specialisation, "Programme", programme2),
            (programme2, "Faculty", faculty),
            (unrelatedCourse, "Programme", unrelatedProgramme)
        };

        bool Matches(ObjectId id, BsonDocument filter) => filter.Elements.All(element => element.Name switch
        {
            "$or" => element.Value.AsBsonArray.Any(f => Matches(id, f.AsBsonDocument)),
            "Properties.LocalAdmin._id" => id == faculty && element.Value.AsObjectId == userId,
            _ => references.Any(r => r.From == id
                                     && element.Name == $"Properties.{r.Property}"
                                     && element.Value["$in"].AsBsonArray.Contains(r.To.ToString()))
        });

        _workflowInstanceRepoMock.Setup(r => r.GetAllByType(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<BsonDocument?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string definition, Dictionary<string, string> _, BsonDocument? filter,
                    CancellationToken _) =>
                filter == null
                    ? []
                    : definitions
                        .Where(d => d.Value == definition && Matches(d.Key, filter))
                        .Select(d => new Dictionary<string, BsonValue> { ["_id"] = d.Key })
                        .ToList());

        return new CourseProgrammeChain(course1, course2, unrelatedCourse);
    }

    private async Task<string> BuildProjectViewFilterJson()
    {
        var authorization = new InstanceAuthorizationFilterService(
            _rightsService, _modelService, _userServiceMock.Object, _workflowInstanceRepoMock.Object);

        var filter = await authorization.BuildAuthorizationFilter("Project", RoleAction.View, _ct);

        Assert.NotNull(filter);
        return filter.ToJson();
    }

    private void AddReferenceProperty(string definition, string propertyName, string referencedDefinition) =>
        _modelService.WorkflowDefinitions[definition].Properties.Add(new ReferencePropertyDefinition
        {
            Name = propertyName,
            Type = referencedDefinition,
            WorkflowDefinition = _modelService.WorkflowDefinitions[referencedDefinition]
        });

    [Fact]
    public async Task BuildAuthorizationFilter_DoesNotThrowOrHang_ForCyclicReferenceBetweenCourses()
    {
        MockCurrentUser();

        var courseDefinition = _modelService.WorkflowDefinitions["Course"];
        courseDefinition.Properties.Add(new ReferencePropertyDefinition
        {
            Name = "RelatedCourse",
            Type = "Course",
            WorkflowDefinition = courseDefinition
        });

        // Every query reports a match, so following RelatedCourse never bottoms out - this
        // simulates a reference chain the type graph considers infinite. Course also already
        // references Programme (existing fixture), so this must cover any definition, not just
        // "Course", or that branch gets an unmocked (null) result.
        _workflowInstanceRepoMock.Setup(r => r.GetAllByType(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<BsonDocument?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Dictionary<string, BsonValue> { ["_id"] = ObjectId.GenerateNewId() }]);

        var authorization = new InstanceAuthorizationFilterService(
            _rightsService, _modelService, _userServiceMock.Object, _workflowInstanceRepoMock.Object);

        // Must terminate (bounded by WorkflowDefinitions.Count) instead of throwing or recursing forever.
        var filter = await authorization.BuildAuthorizationFilter("Project", RoleAction.View, _ct);

        Assert.NotNull(filter);
    }

    private void MockNoVisibleInstances() =>
        _workflowInstanceRepoMock.Setup(r => r.GetAllByType(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<BsonDocument?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

    private void MockVisibleInstancesFor(string workflowDefinition, BsonDocument filter, ObjectId id) =>
        _workflowInstanceRepoMock.Setup(r => r.GetAllByType(
                workflowDefinition,
                It.IsAny<Dictionary<string, string>>(),
                It.Is<BsonDocument?>(actual => actual != null && actual.Equals(filter)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Dictionary<string, BsonValue> { ["_id"] = id }]);
}