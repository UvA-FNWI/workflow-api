using Moq;
using UvA.Workflow.Users;
using UvA.Workflow.Users.EntraId;
using UvA.Workflow.Users.EntraId.Infrastructure;

namespace UvA.Workflow.Tests.Users;

public class EntraLoginMethodClassifierTests
{
    private readonly Mock<IEntraGraphService> _graph = new();
    private readonly Mock<IUserRepository> _users = new();

    [Theory]
    [InlineData(true, "Member", "valid", "employee", "Uva")]
    [InlineData(true, "Member", "valid", "student", "Uva")]
    [InlineData(true, "Guest", null, null, "Uva")]
    [InlineData(true, "Guest", "", "employee", "Uva")]
    [InlineData(false, "Member", "valid", "employee", null)]
    [InlineData(true, "Member", "invalid", "employee", null)]
    public void ClassifyIdentity_ReturnsExpected(bool enabled, string userType, string? state, string? affiliation,
        string? expected)
    {
        var identity = new EntraLoginIdentity(enabled, userType, state, affiliation);
        Assert.Equal(expected, EntraLoginMethodClassifier.ClassifyIdentity(identity));
    }

    [Fact]
    public async Task ClassifyAsync_ReturnsUva_ForEmployeeSam()
    {
        _users.Setup(r => r.GetByExternalId("empsam", It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        _graph.Setup(g => g.GetLoginIdentityBySamAccountNameAsync("empsam", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntraLoginIdentity(true, "Member", "valid", "employee"));

        var classifier = new EntraLoginMethodClassifier(_graph.Object, _users.Object);
        Assert.Equal("Uva", await classifier.ClassifyAsync("empsam"));
    }

    [Fact]
    public async Task ClassifyAsync_ReturnsNull_WhenUserIsExternalInDatabase()
    {
        _users.Setup(r => r.GetByExternalId("invitee", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { UserName = "invitee", ProviderKey = "eduid" });

        var classifier = new EntraLoginMethodClassifier(_graph.Object, _users.Object);
        Assert.Null(await classifier.ClassifyAsync("invitee"));
        _graph.Verify(g => g.GetLoginIdentityBySamAccountNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}