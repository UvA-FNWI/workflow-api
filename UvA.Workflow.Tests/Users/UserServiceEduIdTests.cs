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
                new DataNoseUserDirectory(dataNoseApiClientMock.Object),
                new EduIdUserDirectory()
            ],
            [
                new DataNoseUserSearchSource(dataNoseApiClientMock.Object),
                new RepositoryUserSearchSource(userRepositoryMock.Object)
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
        userRepositoryMock.Setup(r => r.SearchByQueryAndProvider("query", "eduid", CancellationToken.None))
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

        var results = (await service.FindUsers("query", true, CancellationToken.None)).ToArray();

        Assert.Collection(results,
            result =>
            {
                Assert.Equal("internal-1", result.UserName);
                Assert.Equal("duplicate@example.org", result.Email);
                Assert.Equal(DataNoseDirectoryKeys.SourceKey, result.SourceKey);
                Assert.False(result.IsExternal);
            },
            result =>
            {
                Assert.Equal("internal-2", result.UserName);
                Assert.Equal("internal2@example.org", result.Email);
                Assert.Equal(DataNoseDirectoryKeys.SourceKey, result.SourceKey);
                Assert.False(result.IsExternal);
            },
            result =>
            {
                Assert.Equal("external-unique", result.UserName);
                Assert.Equal("unique@example.org", result.Email);
                Assert.Equal(UserSearchSources.Repository, result.SourceKey);
                Assert.True(result.IsExternal);
            });
    }

    [Fact]
    public async Task FindUsers_PassesQueryThroughToRepositorySearch()
    {
        var dataNoseApiClientMock = new Mock<IDataNoseApiClient>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var query = "Doctor";
        dataNoseApiClientMock.Setup(c => c.SearchPeople(query, CancellationToken.None))
            .ReturnsAsync([]);
        userRepositoryMock.Setup(r => r.SearchByQueryAndProvider(query, "eduid", CancellationToken.None))
            .ReturnsAsync([
                new User
                {
                    UserName = "eduid-123",
                    DisplayName = "Doctor Name",
                    Email = "doctor@amsterdamumc.nl",
                    ProviderKey = EduIdDirectoryKeys.ProviderKey
                }
            ]);
        var service = CreateService(dataNoseApiClientMock, userRepositoryMock);

        var result = Assert.Single(await service.FindUsers(query, true, CancellationToken.None));

        Assert.Equal("doctor@amsterdamumc.nl", result.Email);
        userRepositoryMock.Verify(r => r.SearchByQueryAndProvider(query, "eduid", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task FindUsers_PartialEmailAddressQuery_DoesNotReturnStaleInternalRepositoryUser()
    {
        var dataNoseApiClientMock = new Mock<IDataNoseApiClient>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var query = "student@uv";
        dataNoseApiClientMock.Setup(c => c.SearchPeople(query, CancellationToken.None))
            .ReturnsAsync([]);
        userRepositoryMock.Setup(r => r.SearchByQueryAndProvider(query, "eduid", CancellationToken.None))
            .ReturnsAsync([]);
        var service = CreateService(dataNoseApiClientMock, userRepositoryMock);

        Assert.Empty(await service.FindUsers(query, true, CancellationToken.None));

        userRepositoryMock.Verify(r => r.SearchByQueryAndProvider(query, "eduid", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task FindUsers_EmailAddressQuery_DedupesRepositoryUserWhenSearchSourceAlreadyReturnedEmail()
    {
        var dataNoseApiClientMock = new Mock<IDataNoseApiClient>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var query = "uva.nl";
        dataNoseApiClientMock.Setup(c => c.SearchPeople(query, CancellationToken.None))
            .ReturnsAsync([
                new UserSearchResult("student-123",
                    "Student Name",
                    "student@uva.nl",
                    DataNoseDirectoryKeys.SourceKey)
            ]);
        userRepositoryMock.Setup(r => r.SearchByQueryAndProvider(query, "eduid", CancellationToken.None))
            .ReturnsAsync([]);
        var service = CreateService(dataNoseApiClientMock, userRepositoryMock);

        var result = Assert.Single(await service.FindUsers(query, true, CancellationToken.None));

        Assert.Equal(DataNoseDirectoryKeys.SourceKey, result.SourceKey);
    }

    [Fact]
    public async Task FindUsers_FiltersExternalUsersAfterMergeAndDedupe_WhenRequested()
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
        userRepositoryMock.Setup(r => r.SearchByQueryAndProvider("query", "eduid", CancellationToken.None))
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
                }
            ]);
        var service = CreateService(dataNoseApiClientMock, userRepositoryMock);

        var results = (await service.FindUsers("query", false, CancellationToken.None)).ToArray();

        Assert.Collection(results,
            result => Assert.Equal("internal-1", result.UserName),
            result => Assert.Equal("internal-2", result.UserName));
    }

    [Fact]
    public async Task FindUsers_DoesNotSurfaceInternalRepositoryUsers_WhenDataNoseReturnsNoMatches()
    {
        var dataNoseApiClientMock = new Mock<IDataNoseApiClient>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var query = "student@uv";
        dataNoseApiClientMock.Setup(c => c.SearchPeople(query, CancellationToken.None))
            .ReturnsAsync([]);
        userRepositoryMock.Setup(r => r.SearchByQueryAndProvider(query, "eduid", CancellationToken.None))
            .ReturnsAsync([]);
        var service = CreateService(dataNoseApiClientMock, userRepositoryMock);

        Assert.Empty(await service.FindUsers(query, true, CancellationToken.None));

        userRepositoryMock.Verify(r => r.SearchByQueryAndProvider(query, "eduid", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task GetOrganizationForUser_ReturnsOrganization_FromDataNoseLookup()
    {
        var dataNoseApiClientMock = new Mock<IDataNoseApiClient>();
        var userRepositoryMock = new Mock<IUserRepository>();
        dataNoseApiClientMock.Setup(c => c.GetOrganizationForUser("jdoe", CancellationToken.None))
            .ReturnsAsync(new Organization("FNWI", "FNWI"));
        var service = CreateService(dataNoseApiClientMock, userRepositoryMock);

        var organization = await service.GetOrganizationForUser("jdoe", CancellationToken.None);

        Assert.NotNull(organization);
        Assert.Equal("FNWI", organization!.Id);
        Assert.Equal("FNWI", organization.Name);
        dataNoseApiClientMock.Verify(c => c.GetOrganizationForUser("jdoe", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task GetOrganizationForUser_ReturnsNull_WhenDataNoseHasNoOrganization()
    {
        var dataNoseApiClientMock = new Mock<IDataNoseApiClient>();
        var userRepositoryMock = new Mock<IUserRepository>();
        dataNoseApiClientMock.Setup(c => c.GetOrganizationForUser("jdoe", CancellationToken.None))
            .ReturnsAsync((Organization?)null);
        var service = CreateService(dataNoseApiClientMock, userRepositoryMock);

        var organization = await service.GetOrganizationForUser("jdoe", CancellationToken.None);

        Assert.Null(organization);
    }

    [Fact]
    public async Task AddOrUpdateUser_ExternalUser_CreatesWithEduIdProviderKey()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        User? createdUser = null;
        userRepositoryMock.Setup(r => r.GetByExternalId("external-123", CancellationToken.None))
            .ReturnsAsync((User?)null);
        userRepositoryMock.Setup(r => r.Create(It.IsAny<User>(), CancellationToken.None))
            .Callback<User, CancellationToken>((user, _) => createdUser = user)
            .Returns(Task.CompletedTask);
        var service = new UserService(Mock.Of<ICurrentUserAccessor>(),
            userRepositoryMock.Object,
            new MemoryCache(new MemoryCacheOptions()),
            [],
            []);

        var result = await service.AddOrUpdateUser("external-123",
            "External User",
            "external@example.org",
            EduIdDirectoryKeys.ProviderKey,
            null,
            CancellationToken.None);

        Assert.Same(createdUser, result);
        Assert.Equal(EduIdDirectoryKeys.ProviderKey, createdUser?.ProviderKey);
    }

    [Fact]
    public async Task AddOrUpdateUser_ExistingExternalUser_UpdatesProviderKey()
    {
        var user = new User
        {
            UserName = "external-123",
            DisplayName = "External User",
            Email = "external@example.org",
            ProviderKey = UserProviderKeys.Internal
        };
        var userRepositoryMock = new Mock<IUserRepository>();
        userRepositoryMock.Setup(r => r.GetByExternalId("external-123", CancellationToken.None))
            .ReturnsAsync(user);
        var service = new UserService(Mock.Of<ICurrentUserAccessor>(),
            userRepositoryMock.Object,
            new MemoryCache(new MemoryCacheOptions()),
            [],
            []);

        await service.AddOrUpdateUser("external-123",
            "External User",
            "external@example.org",
            EduIdDirectoryKeys.ProviderKey,
            null,
            CancellationToken.None);

        Assert.Equal(EduIdDirectoryKeys.ProviderKey, user.ProviderKey);
        userRepositoryMock.Verify(r => r.Update(user, CancellationToken.None), Times.Once);
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
            [new EduIdUserDirectory()],
            []);
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