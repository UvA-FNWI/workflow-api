using MongoDB.Bson;
using Moq;
using UvA.Workflow.Tests.Impersonation;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Users;

public class RightsServiceCanEditPropertyTests
{
    private static (RightsService rightsService, WorkflowInstance Instance) Build(
        string[] roles,
        Mock<IImpersonationContextService>? impersonationContext = null)
    {
        var modelService = ImpersonationTestHelpers.CreateModelService();
        var userService = new Mock<IUserService>();
        var repository = new Mock<IWorkflowInstanceRepository>();

        userService.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles);
        userService.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = ObjectId.GenerateNewId().ToString(), UserName = "testuser" });

        repository.Setup(r => r.GetAllById(
                It.IsAny<string[]>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var rightsService = new RightsService(
            modelService,
            userService.Object,
            repository.Object,
            impersonationContext?.Object);

        return (rightsService, ImpersonationTestHelpers.CreateProjectInstance());
    }

    [Fact]
    public async Task CanEditProperty_ReturnsFalse_ForDotNotationProperty()
    {
        // Dot-notation paths reference a foreign entity and can never be edited directly,
        var (rightsService, instance) = Build(["SystemAdmin"]);

        var result = await rightsService.CanEditProperty(instance, "Course.Name");

        Assert.False(result);
    }


    [Fact]
    public async Task CanEditProperty_ReturnsFalse_WhenUserHasNoEditRightsAtAll()
    {
        // Student has no edit rights for Title (only property-level rights on SecondReader and PracticalSupervisor).
        var (rightsService, instance) = Build(["Student"]);

        var result = await rightsService.CanEditProperty(instance, "Title");

        Assert.False(result);
    }

    [Fact]
    public async Task CanEditProperty_ReturnsTrue_ViaInstanceLevelEditRights()
    {
        // SystemAdmin has an Edit action with no form and no property restriction, which grants edit on any property.
        var (rightsService, instance) = Build(["SystemAdmin"]);

        var result = await rightsService.CanEditProperty(instance, "Title");

        Assert.True(result);
    }

    [Fact]
    public async Task CanEditProperty_ReturnsTrue_ViaFormLevelEditRights()
    {
        // Coordinator can edit forms: [Start]. Title is a field in the Start form, so editing Title is allowed.
        var (rightsService, instance) = Build(["Coordinator"]);

        var result = await rightsService.CanEditProperty(instance, "Title");

        Assert.True(result);
    }

    [Fact]
    public async Task CanEditProperty_ReturnsFalse_ForPropertyNotInEditableForm()
    {
        // Coordinator can edit forms: [Start], but Report is not a field in Start.
        var (rightsService, instance) = Build(["Coordinator"]);

        var result = await rightsService.CanEditProperty(instance, "Report");

        Assert.False(result);
    }

    [Fact]
    public async Task CanEditProperty_ReturnsTrue_ViaPropertyLevelEditRights()
    {
        // Student has property-level edit rights explicitly on SecondReader.
        var (sut, instance) = Build(["Student"]);

        var result = await sut.CanEditProperty(instance, "SecondReader");

        Assert.True(result);
    }

    [Fact]
    public async Task CanEditProperty_ReturnsTrue_ViaPropertyLevelEditRights_ForArrayProperty()
    {
        // Student also has property-level edit rights on PracticalSupervisor (array).
        var (sut, instance) = Build(["Student"]);

        var result = await sut.CanEditProperty(instance, "PracticalSupervisor");

        Assert.True(result);
    }
}