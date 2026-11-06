using UvA.Workflow.Users;
using UvA.Workflow.Users.EntraId;

namespace UvA.Workflow.Tests.Users;

public class EntraUserDirectoryTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("internal", false)]
    [InlineData("datanose", false)]
    [InlineData("entra", true)]
    [InlineData("entra-id", true)]
    [InlineData("ENTRA-ID", true)]
    public void IsSelected_MatchesEntraProviderNames(string? configured, bool expected)
        => Assert.Equal(expected, EntraDirectoryKeys.IsSelected(configured));

    [Fact]
    public async Task GetRoles_ReturnsEmpty_WithoutReadingGraph()
    {
        var directory = new EntraUserDirectory();
        var user = new User { UserName = "emp1", Email = "emp1@uva.nl", ProviderKey = EntraDirectoryKeys.ProviderKey };

        Assert.Empty(await directory.GetRoles(user, CancellationToken.None));
        Assert.Null(await directory.GetOrganization("emp1", CancellationToken.None));
    }
}