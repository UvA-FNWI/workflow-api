using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using UvA.Workflow.Users;
using UvA.Workflow.Users.EntraId;
using UvA.Workflow.Users.EntraId.Infrastructure;

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
        var graph = new Mock<IEntraGraphService>();
        var directory = new EntraUserDirectory(graph.Object);
        var user = new User { UserName = "emp1", Email = "emp1@uva.nl", ProviderKey = EntraDirectoryKeys.ProviderKey };

        Assert.Empty(await directory.GetRoles(user, CancellationToken.None));
        graph.Verify(g => g.GetUserBySamAccountNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOrganization_ReturnsTheOrganizationFromTheSamLookup()
    {
        var graph = new Mock<IEntraGraphService>();
        graph.Setup(g => g.GetUserBySamAccountNameAsync("emp1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntraUserInfo("emp1@uva.nl", "Emp", "emp1@uva.nl", "emp1", "FNWI | Dir&Dnst/FNW"));
        var directory = new EntraUserDirectory(graph.Object);

        var organization = await directory.GetOrganization("emp1", CancellationToken.None);

        Assert.Equal("FNWI | Dir&Dnst/FNW", organization?.Name);
    }

    [Fact]
    public async Task GetOrganization_ReturnsNull_WhenTheUserHasNoOrganization()
    {
        var graph = new Mock<IEntraGraphService>();
        graph.Setup(g => g.GetUserBySamAccountNameAsync("emp1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntraUserInfo("emp1@uva.nl", "Emp", "emp1@uva.nl", "emp1"));
        var directory = new EntraUserDirectory(graph.Object);

        Assert.Null(await directory.GetOrganization("emp1", CancellationToken.None));
    }

    [Fact]
    public async Task GetRoles_ReturnsSystemAdmin_ForABootstrapUid()
    {
        var options = Options.Create(new SystemAdminBootstrapOptions { BootstrapUids = "Emp1, other" });
        var directory = new EntraUserDirectory(new Mock<IEntraGraphService>().Object, options);

        var roles = await directory.GetRoles(
            new User { UserName = "emp1" }, CancellationToken.None);

        Assert.Equal(SystemAdminBootstrapOptions.RoleName, Assert.Single(roles));
    }

    [Fact]
    public async Task GetOrganization_ReusesTheLookupForADay()
    {
        var graph = new Mock<IEntraGraphService>();
        graph.Setup(g => g.GetUserBySamAccountNameAsync("emp1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntraUserInfo("emp1@uva.nl", "Emp", "emp1@uva.nl", "emp1", "FNWI"));
        var directory = new EntraUserDirectory(graph.Object, cache: new MemoryCache(new MemoryCacheOptions()));

        Assert.Equal("FNWI", (await directory.GetOrganization("emp1", CancellationToken.None))?.Name);
        Assert.Equal("FNWI", (await directory.GetOrganization("emp1", CancellationToken.None))?.Name);

        graph.Verify(g => g.GetUserBySamAccountNameAsync("emp1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrganization_DoesNotCacheADirectoryFailure()
    {
        var graph = new Mock<IEntraGraphService>();
        graph.Setup(g => g.GetUserBySamAccountNameAsync("emp1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DirectoryUnavailableException());
        var directory = new EntraUserDirectory(graph.Object, cache: new MemoryCache(new MemoryCacheOptions()));

        Assert.Null(await directory.GetOrganization("emp1", CancellationToken.None));
        Assert.Null(await directory.GetOrganization("emp1", CancellationToken.None));

        graph.Verify(g => g.GetUserBySamAccountNameAsync("emp1", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}