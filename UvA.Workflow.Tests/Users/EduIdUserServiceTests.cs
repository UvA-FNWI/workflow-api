using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Users;

namespace UvA.Workflow.Tests.Users;

public class EduIdUserServiceTests
{
    private static EduIdUserService CreateService(
        Mock<IUserRepository> userRepositoryMock,
        Mock<IEduIdInvitationClient> invitationClientMock,
        EduIdOptions? options = null)
        => new(userRepositoryMock.Object,
            invitationClientMock.Object,
            Options.Create(options ?? new EduIdOptions()),
            Mock.Of<ILogger<EduIdUserService>>());

    [Fact]
    public async Task InviteUser_CreatesPendingUser_AndBuildsExpectedInvitation()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        var invitationClientMock = new Mock<IEduIdInvitationClient>();
        var options = new EduIdOptions
        {
            RoleIdentifier = 7040,
            InvitationExpiryDays = 30,
            RoleExpiryDays = 365
        };

        User? createdUser = null;
        EduIdInvitationRequest? capturedRequest = null;

        userRepositoryMock.Setup(r => r.GetByEmail("newuser@external.org", CancellationToken.None))
            .ReturnsAsync((User?)null);
        userRepositoryMock.Setup(r => r.Create(It.IsAny<User>(), CancellationToken.None))
            .Callback<User, CancellationToken>((user, _) => createdUser = user)
            .Returns(Task.CompletedTask);
        invitationClientMock.Setup(c =>
                c.CreateInvitationAsync(It.IsAny<EduIdInvitationRequest>(), CancellationToken.None))
            .Callback<EduIdInvitationRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new EduIdInvitationResponse((int)HttpStatusCode.Created,
                [new EduIdRecipientInvitationUrl("newuser@external.org", "https://invite.example/hash")]));

        var service = CreateService(userRepositoryMock, invitationClientMock, options);

        var result = await service.InviteUser("newuser@external.org", "New User", CancellationToken.None);

        Assert.NotNull(createdUser);
        Assert.Equal("newuser@external.org", createdUser!.UserName);
        Assert.Equal("New User", createdUser.DisplayName);
        Assert.Equal("newuser@external.org", createdUser.Email);
        Assert.Equal(UserAuthProvider.EduId, createdUser.AuthProvider);
        Assert.False(createdUser.IsActive);

        Assert.NotNull(capturedRequest);
        Assert.Equal("GUEST", capturedRequest!.IntendedAuthority);
        Assert.Equal(string.Empty, capturedRequest.Message);
        Assert.Equal("en", capturedRequest.Language);
        Assert.True(capturedRequest.EnforceEmailEquality);
        Assert.True(capturedRequest.EduIdOnly);
        Assert.True(capturedRequest.GuestRoleIncluded);
        Assert.True(capturedRequest.SuppressSendingEmails);
        Assert.Equal(["newuser@external.org"], capturedRequest.Invites);
        Assert.Equal([7040], capturedRequest.RoleIdentifiers);
        Assert.NotNull(capturedRequest.RoleExpiryDate);
        Assert.Equal("https://invite.example/hash", result.InvitationUrl);
    }

    [Fact]
    public async Task InviteUser_InternalEmail_Throws()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        var invitationClientMock = new Mock<IEduIdInvitationClient>();
        var service = CreateService(userRepositoryMock, invitationClientMock,
            new EduIdOptions { InternalEmailDomains = ["internal.org"] });

        var ex = await Assert.ThrowsAsync<EduIdInviteException>(() =>
            service.InviteUser("person@internal.org", "Internal Person", CancellationToken.None));

        Assert.Equal(EduIdInviteFailureReason.InternalEmail, ex.Reason);
        invitationClientMock.Verify(
            c => c.CreateInvitationAsync(It.IsAny<EduIdInvitationRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        userRepositoryMock.Verify(r => r.Create(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InviteUser_PendingUser_Throws()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        var invitationClientMock = new Mock<IEduIdInvitationClient>();
        userRepositoryMock.Setup(r => r.GetByEmail("pending@external.org", CancellationToken.None))
            .ReturnsAsync(new User
            {
                UserName = "pending@external.org",
                Email = "pending@external.org",
                AuthProvider = UserAuthProvider.EduId,
                IsActive = false
            });

        var service = CreateService(userRepositoryMock, invitationClientMock);

        var ex = await Assert.ThrowsAsync<EduIdInviteException>(() =>
            service.InviteUser("pending@external.org", "Pending User", CancellationToken.None));

        Assert.Equal(EduIdInviteFailureReason.PendingInvitation, ex.Reason);
    }

    [Fact]
    public async Task InviteUser_ActiveUser_Throws()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        var invitationClientMock = new Mock<IEduIdInvitationClient>();
        userRepositoryMock.Setup(r => r.GetByEmail("active@external.org", CancellationToken.None))
            .ReturnsAsync(new User
            {
                UserName = "active-user",
                Email = "active@external.org",
                AuthProvider = UserAuthProvider.EduId,
                IsActive = true
            });

        var service = CreateService(userRepositoryMock, invitationClientMock);

        var ex = await Assert.ThrowsAsync<EduIdInviteException>(() =>
            service.InviteUser("active@external.org", "Active User", CancellationToken.None));

        Assert.Equal(EduIdInviteFailureReason.UserAlreadyExists, ex.Reason);
    }

    [Fact]
    public async Task InviteUser_MissingInvitationUrl_ThrowsAndDoesNotPersistPendingUser()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        var invitationClientMock = new Mock<IEduIdInvitationClient>();

        userRepositoryMock.Setup(r => r.GetByEmail("newuser@external.org", CancellationToken.None))
            .ReturnsAsync((User?)null);
        invitationClientMock.Setup(c =>
                c.CreateInvitationAsync(It.IsAny<EduIdInvitationRequest>(), CancellationToken.None))
            .ReturnsAsync(new EduIdInvitationResponse((int)HttpStatusCode.Created, []));

        var service = CreateService(userRepositoryMock, invitationClientMock);

        var ex = await Assert.ThrowsAsync<EduIdInviteException>(() =>
            service.InviteUser("newuser@external.org", "New User", CancellationToken.None));

        Assert.Equal(EduIdInviteFailureReason.MissingInvitationUrl, ex.Reason);
        userRepositoryMock.Verify(r => r.Create(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InviteUser_UnexpectedInvitationStatus_ThrowsAndDoesNotPersistPendingUser()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        var invitationClientMock = new Mock<IEduIdInvitationClient>();

        userRepositoryMock.Setup(r => r.GetByEmail("newuser@external.org", CancellationToken.None))
            .ReturnsAsync((User?)null);
        invitationClientMock.Setup(c =>
                c.CreateInvitationAsync(It.IsAny<EduIdInvitationRequest>(), CancellationToken.None))
            .ReturnsAsync(new EduIdInvitationResponse((int)HttpStatusCode.BadRequest, null));

        var service = CreateService(userRepositoryMock, invitationClientMock);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.InviteUser("newuser@external.org", "New User", CancellationToken.None));

        userRepositoryMock.Verify(r => r.Create(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAuthenticatedUser_ActivatesPendingUserFoundByEmail()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        var invitationClientMock = new Mock<IEduIdInvitationClient>();
        var pendingUser = new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserName = "pending@external.org",
            DisplayName = "Pending User",
            Email = "pending@external.org",
            AuthProvider = UserAuthProvider.EduId,
            IsActive = false
        };

        userRepositoryMock.Setup(r => r.GetByExternalId("eduid-123", CancellationToken.None))
            .ReturnsAsync((User?)null);
        userRepositoryMock.Setup(r => r.GetByEmailAndProvider("pending@external.org", UserAuthProvider.EduId,
                CancellationToken.None))
            .ReturnsAsync(pendingUser);
        userRepositoryMock.Setup(r => r.Update(pendingUser, CancellationToken.None))
            .Returns(Task.CompletedTask);

        var service = CreateService(userRepositoryMock, invitationClientMock);

        var result = await service.ResolveAuthenticatedUser("eduid-123",
            "Activated User",
            "pending@external.org",
            CancellationToken.None);

        Assert.Same(pendingUser, result);
        Assert.Equal("eduid-123", pendingUser.UserName);
        Assert.Equal("Activated User", pendingUser.DisplayName);
        Assert.Equal("pending@external.org", pendingUser.Email);
        Assert.True(pendingUser.IsActive);
        userRepositoryMock.Verify(r => r.Update(pendingUser, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ResolveAuthenticatedUser_ExistingEduIdByUidWithoutChanges_DoesNotUpdate()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        var invitationClientMock = new Mock<IEduIdInvitationClient>();
        var existingUser = new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserName = "eduid-123",
            DisplayName = "External User",
            Email = "external@example.org",
            AuthProvider = UserAuthProvider.EduId,
            IsActive = true
        };

        userRepositoryMock.Setup(r => r.GetByExternalId("eduid-123", CancellationToken.None))
            .ReturnsAsync(existingUser);

        var service = CreateService(userRepositoryMock, invitationClientMock);

        var result = await service.ResolveAuthenticatedUser("eduid-123",
            "External User",
            "external@example.org",
            CancellationToken.None);

        Assert.Same(existingUser, result);
        userRepositoryMock.Verify(r => r.Update(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAuthenticatedUser_WithoutEmailAndNoUidMatch_ReturnsNull()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        var invitationClientMock = new Mock<IEduIdInvitationClient>();

        userRepositoryMock.Setup(r => r.GetByExternalId("eduid-123", CancellationToken.None))
            .ReturnsAsync((User?)null);

        var service = CreateService(userRepositoryMock, invitationClientMock);

        var result = await service.ResolveAuthenticatedUser("eduid-123",
            "External User",
            null,
            CancellationToken.None);

        Assert.Null(result);
        userRepositoryMock.Verify(r => r.GetByEmailAndProvider(It.IsAny<string>(), It.IsAny<UserAuthProvider>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}