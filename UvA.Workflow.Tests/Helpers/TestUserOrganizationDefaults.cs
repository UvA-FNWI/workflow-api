using UvA.Workflow.Api.Users;
using UvA.Workflow.Users;

namespace UvA.Workflow.Tests.Helpers;

public static class TestUserOrganizationDefaults
{
    public static IUserOrganizationDefaults Instance { get; } = new UserOrganizationDefaults();
}