using Moq;
using UvA.Workflow.Users.EntraId;
using UvA.Workflow.Users.EntraId.Infrastructure;

namespace UvA.Workflow.Tests.Users;

public class EntraUserSearchSourceTests
{
    [Fact]
    public async Task FindUsers_MapsSamAccountNameToUserName_MatchingSurfConextUid()
    {
        var graph = new Mock<IEntraGraphService>();
        graph.Setup(g => g.SearchUsersAsync("emp", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new EntraUserInfo("Emp.One@uva.nl", "Emp One", "emp1@uva.nl", "emp1", "emp1")
            ]);
        var source = new EntraUserSearchSource(graph.Object);

        var result = Assert.Single(await source.FindUsers("emp", CancellationToken.None));

        Assert.Equal("emp1", result.UserName);
        Assert.Equal("emp1@uva.nl", result.Email);
        Assert.Equal(EntraDirectoryKeys.SourceKey, result.SourceKey);
        Assert.False(result.IsExternal);
    }

    [Fact]
    public async Task FindUsers_FallsBackToUpnLocalPart_WhenSamAccountNameMissing()
    {
        var graph = new Mock<IEntraGraphService>();
        graph.Setup(g => g.SearchUsersAsync("one", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new EntraUserInfo("emp1@uva.nl", "Emp One", "emp1@uva.nl")
            ]);
        var source = new EntraUserSearchSource(graph.Object);

        var result = Assert.Single(await source.FindUsers("one", CancellationToken.None));

        Assert.Equal("emp1", result.UserName);
    }

    [Fact]
    public async Task FindUsers_KeepsGuestUpn_WhenExtAccount()
    {
        var graph = new Mock<IEntraGraphService>();
        graph.Setup(g => g.SearchUsersAsync("guest", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new EntraUserInfo(
                    "user_gmail.com#EXT#@tenant.onmicrosoft.com",
                    "Guest",
                    "user@gmail.com")
            ]);
        var source = new EntraUserSearchSource(graph.Object);

        var result = Assert.Single(await source.FindUsers("guest", CancellationToken.None));

        Assert.Equal("user_gmail.com#EXT#@tenant.onmicrosoft.com", result.UserName);
        Assert.Equal("user@gmail.com", result.Email);
    }

    [Fact]
    public async Task FindUsers_ReturnsEmpty_WhenQueryBlank()
    {
        var graph = new Mock<IEntraGraphService>();
        var source = new EntraUserSearchSource(graph.Object);

        Assert.Empty(await source.FindUsers("  ", CancellationToken.None));
        graph.Verify(g => g.SearchUsersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}