using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using UvA.Workflow.Users;
using UvA.Workflow.Users.EduId;

namespace UvA.Workflow.Tests.Users;

public class EduIdUserServiceExternalUserTests
{
    private static EduIdUserService CreateService(
        Mock<IUserRepository> userRepositoryMock,
        EduIdOptions? options = null)
        => new(userRepositoryMock.Object,
            Mock.Of<IEduIdInvitationClient>(),
            Options.Create(options ?? new EduIdOptions()),
            Mock.Of<ILogger<EduIdUserService>>());

    [Fact]
    public async Task CreateOrUpdateExternalUser_CreatesInactiveEduIdUser()
    {
        var organization = new Organization("org-1", "External Org");
        var userRepositoryMock = new Mock<IUserRepository>();
        User? createdUser = null;
        userRepositoryMock.Setup(r => r.GetByEmail("external@example.org", CancellationToken.None))
            .ReturnsAsync((User?)null);
        userRepositoryMock.Setup(r => r.Create(It.IsAny<User>(), CancellationToken.None))
            .Callback<User, CancellationToken>((user, _) => createdUser = user)
            .Returns(Task.CompletedTask);
        var service = CreateService(userRepositoryMock,
            new EduIdOptions { InternalEmailDomains = ["uva.nl"] });

        var result = await service.CreateOrUpdateExternalUser(
            " External User ",
            " external@example.org ",
            organization,
            CancellationToken.None);

        Assert.NotNull(createdUser);
        Assert.Equal("external@example.org", createdUser.UserName);
        Assert.Equal("External User", createdUser.DisplayName);
        Assert.Equal("external@example.org", createdUser.Email);
        Assert.Equal(EduIdDirectoryKeys.ProviderKey, createdUser.ProviderKey);
        Assert.Same(organization, createdUser.Organization);
        Assert.False(createdUser.IsActive);
        Assert.Equal(UserSearchSources.Repository, result.SourceKey);
        Assert.True(result.IsExternal);
    }

    [Fact]
    public async Task CreateOrUpdateExternalUser_RejectsInvalidEmail()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        var service = CreateService(userRepositoryMock);

        var ex = await Assert.ThrowsAsync<ExternalUserCreationException>(() =>
            service.CreateOrUpdateExternalUser("External User", "not-an-email", null, CancellationToken.None));

        Assert.Equal(ExternalUserCreationFailureReason.InvalidEmailAddress, ex.Reason);
        userRepositoryMock.Verify(r => r.Create(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("student@uva.nl")]
    [InlineData("student@sub.uva.nl")]
    public async Task CreateOrUpdateExternalUser_RejectsInternalEmail(string email)
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        var service = CreateService(userRepositoryMock,
            new EduIdOptions { InternalEmailDomains = ["uva.nl"] });

        var ex = await Assert.ThrowsAsync<ExternalUserCreationException>(() =>
            service.CreateOrUpdateExternalUser("External User", email, null, CancellationToken.None));

        Assert.Equal(ExternalUserCreationFailureReason.InternalEmailAddress, ex.Reason);
        userRepositoryMock.Verify(r => r.Create(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateOrUpdateExternalUser_RejectsActiveDuplicate()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        userRepositoryMock.Setup(r => r.GetByEmail("external@example.org", CancellationToken.None))
            .ReturnsAsync(new User
            {
                UserName = "external@example.org",
                DisplayName = "External User",
                Email = "external@example.org",
                ProviderKey = EduIdDirectoryKeys.ProviderKey,
                IsActive = true
            });
        var service = CreateService(userRepositoryMock);

        var ex = await Assert.ThrowsAsync<ExternalUserCreationException>(() =>
            service.CreateOrUpdateExternalUser(
                "External User",
                "external@example.org",
                null,
                CancellationToken.None));

        Assert.Equal(ExternalUserCreationFailureReason.UserAlreadyExists, ex.Reason);
        userRepositoryMock.Verify(r => r.Update(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateOrUpdateExternalUser_UpdatesInactiveEduIdDuplicate()
    {
        var organization = new Organization("org-1", "External Org");
        var existingUser = new User
        {
            UserName = "external@example.org",
            DisplayName = "Old Name",
            Email = "external@example.org",
            ProviderKey = EduIdDirectoryKeys.ProviderKey,
            IsActive = false
        };
        var userRepositoryMock = new Mock<IUserRepository>();
        userRepositoryMock.Setup(r => r.GetByEmail("external@example.org", CancellationToken.None))
            .ReturnsAsync(existingUser);
        userRepositoryMock.Setup(r => r.Update(existingUser, CancellationToken.None))
            .Returns(Task.CompletedTask);
        var service = CreateService(userRepositoryMock);

        var result = await service.CreateOrUpdateExternalUser(
            "New Name",
            "external@example.org",
            organization,
            CancellationToken.None);

        Assert.Equal("New Name", existingUser.DisplayName);
        Assert.Same(organization, existingUser.Organization);
        Assert.Equal(EduIdDirectoryKeys.ProviderKey, existingUser.ProviderKey);
        Assert.False(existingUser.IsActive);
        Assert.Equal("New Name", result.DisplayName);
        Assert.True(result.IsExternal);
        userRepositoryMock.Verify(r => r.Update(existingUser, CancellationToken.None), Times.Once);
    }
}