using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Users;
using UvA.Workflow.Users.DataNose;
using UvA.Workflow.Users.EduId;

namespace UvA.Workflow.Tests.Users;

public class UserServiceEduIdTests
{
    private static UserService CreateService(
        Mock<IDataNoseApiClient> dataNoseApiClientMock,
        Mock<IUserRepository> userRepositoryMock)
        => new(Mock.Of<ICurrentUserAccessor>(),
            userRepositoryMock.Object,
            new MemoryCache(new MemoryCacheOptions()),
            [
                new DataNoseUserRoleSource(dataNoseApiClientMock.Object),
                new EduIdUserDirectory(userRepositoryMock.Object)
            ],
            [
                new DataNoseUserSearchSource(dataNoseApiClientMock.Object),
                new EduIdUserDirectory(userRepositoryMock.Object)
            ]);

    [Fact]
    public async Task GetRoles_EduIdUser_ReturnsEmpty_WithoutCallingDataNose()
    {
        var dataNoseApiClientMock = new Mock<IDataNoseApiClient>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var service = CreateService(dataNoseApiClientMock, userRepositoryMock);
        var user = new User
        {
            UserName = "eduid-123",
            DisplayName = "External User",
            Email = "external@example.org",
            ProviderKey = EduIdDirectoryKeys.ProviderKey,
            IsActive = true
        };

        var roles = await service.GetRoles(user, CancellationToken.None);

        Assert.Empty(roles);
        dataNoseApiClientMock.Verify(c => c.GetRolesByUser(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FindUsers_MergesEduIdUsers_AndDedupesByEmailThenUserName()
    {
        var dataNoseApiClientMock = new Mock<IDataNoseApiClient>();
        var userRepositoryMock = new Mock<IUserRepository>();
        dataNoseApiClientMock.Setup(c => c.SearchPeople("query", CancellationToken.None))
            .ReturnsAsync([
                new UserSearchResult("internal-1", "Internal One", "duplicate@example.org",
                    DataNoseDirectoryKeys.SourceKey),
                new UserSearchResult("internal-2", "Internal Two", "internal2@example.org",
                    DataNoseDirectoryKeys.SourceKey)
            ]);
        userRepositoryMock.Setup(r => r.SearchByQuery("query", EduIdDirectoryKeys.ProviderKey, CancellationToken.None))
            .ReturnsAsync([
                new User
                {
                    UserName = "external-duplicate",
                    DisplayName = "External Duplicate",
                    Email = "duplicate@example.org",
                    ProviderKey = EduIdDirectoryKeys.ProviderKey
                },
                new User
                {
                    UserName = "external-unique",
                    DisplayName = "External Unique",
                    Email = "unique@example.org",
                    ProviderKey = EduIdDirectoryKeys.ProviderKey
                },
                new User
                {
                    UserName = "internal-2",
                    DisplayName = "External Username Duplicate",
                    Email = "other@example.org",
                    ProviderKey = EduIdDirectoryKeys.ProviderKey
                }
            ]);

        var service = CreateService(dataNoseApiClientMock, userRepositoryMock);

        var results = (await service.FindUsers("query", CancellationToken.None)).ToArray();

        Assert.Collection(results,
            result =>
            {
                Assert.Equal("internal-1", result.UserName);
                Assert.Equal("duplicate@example.org", result.Email);
                Assert.Equal(DataNoseDirectoryKeys.SourceKey, result.SourceKey);
            },
            result =>
            {
                Assert.Equal("internal-2", result.UserName);
                Assert.Equal("internal2@example.org", result.Email);
                Assert.Equal(DataNoseDirectoryKeys.SourceKey, result.SourceKey);
            },
            result =>
            {
                Assert.Equal("external-unique", result.UserName);
                Assert.Equal("unique@example.org", result.Email);
                Assert.Equal(EduIdDirectoryKeys.SourceKey, result.SourceKey);
            });
    }

    [Fact]
    public async Task GetRoles_InternalUser_UsesDataNoseRoleSource()
    {
        var dataNoseApiClientMock = new Mock<IDataNoseApiClient>();
        var userRepositoryMock = new Mock<IUserRepository>();
        dataNoseApiClientMock.Setup(c => c.GetRolesByUser("internal-123", CancellationToken.None))
            .ReturnsAsync(["Coordinator"]);
        var service = CreateService(dataNoseApiClientMock, userRepositoryMock);
        var user = new User
        {
            UserName = "internal-123",
            DisplayName = "Internal User",
            Email = "internal@example.org",
            ProviderKey = UserProviderKeys.Internal,
            IsActive = true
        };

        var roles = (await service.GetRoles(user, CancellationToken.None)).ToArray();

        Assert.Equal(["Coordinator"], roles);
        dataNoseApiClientMock.Verify(c => c.GetRolesByUser("internal-123", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task GetRoles_UnknownProvider_ReturnsEmpty_WhenNoSourceResolves()
    {
        var dataNoseApiClientMock = new Mock<IDataNoseApiClient>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var service = new UserService(Mock.Of<ICurrentUserAccessor>(),
            userRepositoryMock.Object,
            new MemoryCache(new MemoryCacheOptions()),
            [new EduIdUserDirectory(userRepositoryMock.Object)],
            [new EduIdUserDirectory(userRepositoryMock.Object)]);
        var user = new User
        {
            UserName = "unknown-123",
            DisplayName = "Unknown User",
            Email = "unknown@example.org",
            ProviderKey = "other-provider",
            IsActive = true
        };

        var roles = await service.GetRoles(user, CancellationToken.None);

        Assert.Empty(roles);
        dataNoseApiClientMock.Verify(c => c.GetRolesByUser(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}