using Moq;
using UvA.Workflow.Tests.WorkflowInstances;
using UvA.Workflow.Users;

namespace UvA.Workflow.Tests.Users;

public class LinkedStepRightsTests
{
    [Fact]
    public async Task DownstreamSubmissionTracksSourceCompletionAndUndoWithoutOpeningTheHost()
    {
        var files = LinkedStepConfigurationTests.Files();
        files["Common/Roles/Registered.yaml"] = "name: Registered";
        files["Individual/Steps/After.yaml"] +=
            "\nactions:\n  - type: Submit\n    roles: [Registered]\n    form: AfterForm";
        files["Individual/Forms/AfterForm.yaml"] = "name: AfterForm\nstep: After";
        var setup = new LinkedStepTestSetup(files);
        setup.Student.CurrentStep = "After";
        setup.Complete("Uploaded", 20);
        var users = new Mock<IUserService>();
        users.Setup(service => service.GetRolesOfCurrentUser()).ReturnsAsync(Array.Empty<string>());
        var rights = new RightsService(setup.Model, users.Object, setup.Repository.Object,
            UvA.Workflow.Tests.Helpers.UnitTestsHelpers.CreateStateResolver(setup.Model, setup.Repository.Object));

        Assert.False(await rights.Can(setup.Student, RoleAction.Submit, "AfterForm"));
        setup.Complete("Assessed", 21);
        Assert.True(await rights.Can(setup.Student, RoleAction.Submit, "AfterForm"));
        setup.Source.Events.Remove("Assessed");
        Assert.False(await rights.Can(setup.Student, RoleAction.Submit, "AfterForm"));
        Assert.False(await rights.Can(setup.Source, RoleAction.View));
    }
}