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
                new EntraUserInfo("Emp.One@uva.nl", "Emp One", "emp1@uva.nl", "emp1")
            ]);
        var source = new EntraUserSearchSource(graph.Object);

        var result = Assert.Single(await source.FindUsers("emp", CancellationToken.None));

        Assert.Equal("emp1", result.UserName);
        Assert.Equal("emp1@uva.nl", result.Email);
        Assert.Equal(EntraDirectoryKeys.SourceKey, result.SourceKey);
        Assert.False(result.IsExternal);
    }

    [Fact]
    public async Task FindUsers_KeepsMixedCaseSamAccountName()
    {
        var graph = new Mock<IEntraGraphService>();
        graph.Setup(g => g.SearchUsersAsync("Emp", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new EntraUserInfo("Emp.One@uva.nl", "Emp One", "emp1@uva.nl", " Emp1 ")
            ]);
        var source = new EntraUserSearchSource(graph.Object);

        var result = Assert.Single(await source.FindUsers("Emp", CancellationToken.None));

        Assert.Equal("Emp1", result.UserName);
    }

    [Fact]
    public async Task FindUsers_OmitsUser_WhenSamAccountNameMissing()
    {
        var graph = new Mock<IEntraGraphService>();
        graph.Setup(g => g.SearchUsersAsync("one", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new EntraUserInfo("emp1@uva.nl", "Emp One", "emp1@uva.nl"),
                new EntraUserInfo(
                    "user_gmail.com#EXT#@tenant.onmicrosoft.com",
                    "Guest",
                    "user@gmail.com")
            ]);
        var source = new EntraUserSearchSource(graph.Object);

        Assert.Empty(await source.FindUsers("one", CancellationToken.None));
    }

    [Fact]
    public async Task FindUsers_OmitsSamAccountName_WhenMoreThanOneUserHasIt()
    {
        var graph = new Mock<IEntraGraphService>();
        graph.Setup(g => g.SearchUsersAsync("emp", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new EntraUserInfo("a@uva.nl", "A", "a@uva.nl", "Emp1"),
                new EntraUserInfo("b@uva.nl", "B", "b@uva.nl", "emp1"),
                new EntraUserInfo("c@uva.nl", "C", "c@uva.nl", "other1")
            ]);
        var source = new EntraUserSearchSource(graph.Object);

        var result = Assert.Single(await source.FindUsers("emp", CancellationToken.None));

        Assert.Equal("other1", result.UserName);
    }

    [Fact]
    public async Task FindUsers_SetsTheOrganization_WhenGraphReturnsOne()
    {
        var graph = new Mock<IEntraGraphService>();
        graph.Setup(g => g.SearchUsersAsync("emp", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new EntraUserInfo("Emp.One@uva.nl", "Emp One", "emp1@uva.nl", "emp1", "FNWI | Dir&Dnst/FNW")
            ]);
        var source = new EntraUserSearchSource(graph.Object);

        var result = Assert.Single(await source.FindUsers("emp", CancellationToken.None));

        Assert.Equal("FNWI | Dir&Dnst/FNW", result.Organization?.Name);
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