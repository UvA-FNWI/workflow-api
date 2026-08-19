using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Organizations;
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
        var organization = new Organization { Id = ObjectId.GenerateNewId().ToString(), Name = "External Org" };
        var userRepositoryMock = new Mock<IUserRepository>();
        User? createdUser = null;
        userRepositoryMock.Setup(r => r.Create(It.IsAny<User>(), CancellationToken.None))
            .Callback<User, CancellationToken>((user, _) => createdUser = user)
            .Returns(Task.CompletedTask);
        var service = CreateService(userRepositoryMock,
            new EduIdOptions { InternalEmailDomains = ["uva.nl"] });

        var result = await service.CreateOrUpdateExternalUser(
            " External User ",
            " external@example.org ",
            organization,
            null,
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
            service.CreateOrUpdateExternalUser("External User", "not-an-email", null, null, CancellationToken.None));

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
            service.CreateOrUpdateExternalUser("External User", email, null, null, CancellationToken.None));

        Assert.Equal(ExternalUserCreationFailureReason.InternalEmailAddress, ex.Reason);
        userRepositoryMock.Verify(r => r.Create(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateOrUpdateExternalUser_RejectsActiveDuplicate()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var userRepositoryMock = new Mock<IUserRepository>();
        userRepositoryMock.Setup(r => r.GetById(userId, CancellationToken.None))
            .ReturnsAsync(new User
            {
                Id = userId,
                UserName = "external@example.org",
                DisplayName = "External User",
                Email = "external@example.org",
                ProviderKey = EduIdDirectoryKeys.ProviderKey,
                IsActive = true,
                InvitationState = UserInvitationState.Completed
            });
        var service = CreateService(userRepositoryMock);

        var ex = await Assert.ThrowsAsync<ExternalUserCreationException>(() =>
            service.CreateOrUpdateExternalUser(
                "External User",
                "external@example.org",
                null,
                userId,
                CancellationToken.None));

        Assert.Equal(ExternalUserCreationFailureReason.UserAlreadyExists, ex.Reason);
        userRepositoryMock.Verify(r => r.Update(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateOrUpdateExternalUser_UpdatesInactiveEduIdDuplicate()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var organization = new Organization { Id = ObjectId.GenerateNewId().ToString(), Name = "External Org" };
        var existingUser = new User
        {
            Id = userId,
            UserName = "external@example.org",
            DisplayName = "Old Name",
            Email = "external@example.org",
            ProviderKey = EduIdDirectoryKeys.ProviderKey,
            IsActive = false,
            InvitationState = UserInvitationState.Required
        };
        var userRepositoryMock = new Mock<IUserRepository>();
        userRepositoryMock.Setup(r => r.GetById(userId, CancellationToken.None))
            .ReturnsAsync(existingUser);
        userRepositoryMock.Setup(r => r.Update(existingUser, CancellationToken.None))
            .Returns(Task.CompletedTask);
        var service = CreateService(userRepositoryMock);

        var result = await service.CreateOrUpdateExternalUser(
            "New Name",
            "external@example.org",
            organization,
            userId,
            CancellationToken.None);

        Assert.Equal("New Name", existingUser.DisplayName);
        Assert.Same(organization, existingUser.Organization);
        Assert.Equal(EduIdDirectoryKeys.ProviderKey, existingUser.ProviderKey);
        Assert.False(existingUser.IsActive);
        Assert.Equal("New Name", result.DisplayName);
        Assert.True(result.IsExternal);
        userRepositoryMock.Verify(r => r.Update(existingUser, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task CreateOrUpdateExternalUser_ThrowsInvalidUserId_WhenUserNotFound()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var userRepositoryMock = new Mock<IUserRepository>();
        userRepositoryMock.Setup(r => r.GetById(userId, CancellationToken.None))
            .ReturnsAsync((User?)null);
        var service = CreateService(userRepositoryMock);

        var ex = await Assert.ThrowsAsync<ExternalUserCreationException>(() =>
            service.CreateOrUpdateExternalUser("Name", "external@example.org", null, userId, CancellationToken.None));

        Assert.Equal(ExternalUserCreationFailureReason.InvalidUserId, ex.Reason);
        userRepositoryMock.Verify(r => r.Create(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateOrUpdateExternalUser_NoChanges_DoesNotCallUpdate()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var organization = new Organization { Id = ObjectId.GenerateNewId().ToString() };
        var existingUser = new User
        {
            Id = userId,
            UserName = "external@example.org",
            DisplayName = "Same Name",
            Email = "external@example.org",
            ProviderKey = EduIdDirectoryKeys.ProviderKey,
            Organization = organization,
            IsActive = false,
            InvitationState = UserInvitationState.Required
        };
        var userRepositoryMock = new Mock<IUserRepository>();
        userRepositoryMock.Setup(r => r.GetById(userId, CancellationToken.None)).ReturnsAsync(existingUser);
        var service = CreateService(userRepositoryMock);

        await service.CreateOrUpdateExternalUser("Same Name", "external@example.org", organization, userId,
            CancellationToken.None);

        userRepositoryMock.Verify(r => r.Update(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateOrUpdateExternalUser_PendingUser_EmailChanged_SendsInvite()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var existingUser = new User
        {
            Id = userId,
            UserName = "old@example.org",
            DisplayName = "Name",
            Email = "old@example.org",
            ProviderKey = EduIdDirectoryKeys.ProviderKey,
            IsActive = false,
            InvitationState = UserInvitationState.Pending
        };
        var userRepositoryMock = new Mock<IUserRepository>();
        userRepositoryMock.Setup(r => r.GetById(userId, CancellationToken.None)).ReturnsAsync(existingUser);
        userRepositoryMock.Setup(r => r.Update(existingUser, CancellationToken.None)).Returns(Task.CompletedTask);
        var invitationClientMock = new Mock<IEduIdInvitationClient>();
        invitationClientMock
            .Setup(c => c.CreateInvitationAsync(It.IsAny<EduIdInvitationRequest>(), CancellationToken.None))
            .ReturnsAsync(new EduIdInvitationResponse(200, null));

        var service = new EduIdUserService(
            userRepositoryMock.Object,
            invitationClientMock.Object,
            Options.Create(new EduIdOptions()),
            Mock.Of<ILogger<EduIdUserService>>());

        await service.CreateOrUpdateExternalUser("Name", "new@example.org", null, userId, CancellationToken.None);

        Assert.Equal("new@example.org", existingUser.Email);
        userRepositoryMock.Verify(r => r.Update(existingUser, CancellationToken.None), Times.Once);
        invitationClientMock.Verify(
            c => c.CreateInvitationAsync(
                It.Is<EduIdInvitationRequest>(r => r.Invites.Contains("new@example.org")),
                CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task CreateOrUpdateExternalUser_PendingUser_DisplayNameAndOrgIgnored()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var existingUser = new User
        {
            Id = userId,
            UserName = "external@example.org",
            DisplayName = "Original Name",
            Email = "external@example.org",
            ProviderKey = EduIdDirectoryKeys.ProviderKey,
            IsActive = false,
            InvitationState = UserInvitationState.Pending
        };
        var userRepositoryMock = new Mock<IUserRepository>();
        userRepositoryMock.Setup(r => r.GetById(userId, CancellationToken.None)).ReturnsAsync(existingUser);
        var service = CreateService(userRepositoryMock);

        await service.CreateOrUpdateExternalUser(
            "New Name", "external@example.org",
            new Organization { Name = "New Org" },
            userId, CancellationToken.None);

        Assert.Equal("Original Name", existingUser.DisplayName);
        Assert.Null(existingUser.Organization);
        userRepositoryMock.Verify(r => r.Update(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateOrUpdateExternalUser_BlankDisplayName_FallsBackToEmail()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        User? createdUser = null;
        userRepositoryMock.Setup(r => r.Create(It.IsAny<User>(), CancellationToken.None))
            .Callback<User, CancellationToken>((u, _) => createdUser = u)
            .Returns(Task.CompletedTask);
        var service = CreateService(userRepositoryMock);

        await service.CreateOrUpdateExternalUser("   ", "external@example.org", null, null, CancellationToken.None);

        Assert.NotNull(createdUser);
        Assert.Equal("external@example.org", createdUser.DisplayName);
    }
}