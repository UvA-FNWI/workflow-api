using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.DataNose;
using UvA.Workflow.Users;

namespace UvA.Workflow.Tests.Users;

public class UserServiceEduIdTests
{
    private static UserService CreateService(
        Mock<IDataNoseApiClient> dataNoseApiClientMock,
        Mock<IUserRepository> userRepositoryMock)
        => new(Mock.Of<IHttpContextAccessor>(),
            dataNoseApiClientMock.Object,
            userRepositoryMock.Object,
            new MemoryCache(new MemoryCacheOptions()),
            [new EduIdUserDirectory(userRepositoryMock.Object)],
            [new EduIdUserDirectory(userRepositoryMock.Object)]);

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
            AuthProvider = UserAuthProvider.EduId,
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
                new UserSearchResult("internal-1", "Internal One", "duplicate@example.org"),
                new UserSearchResult("internal-2", "Internal Two", "internal2@example.org")
            ]);
        userRepositoryMock.Setup(r => r.SearchByQuery("query", UserAuthProvider.EduId, CancellationToken.None))
            .ReturnsAsync([
                new User
                {
                    UserName = "external-duplicate",
                    DisplayName = "External Duplicate",
                    Email = "duplicate@example.org",
                    AuthProvider = UserAuthProvider.EduId
                },
                new User
                {
                    UserName = "external-unique",
                    DisplayName = "External Unique",
                    Email = "unique@example.org",
                    AuthProvider = UserAuthProvider.EduId
                },
                new User
                {
                    UserName = "internal-2",
                    DisplayName = "External Username Duplicate",
                    Email = "other@example.org",
                    AuthProvider = UserAuthProvider.EduId
                }
            ]);

        var service = CreateService(dataNoseApiClientMock, userRepositoryMock);

        var results = (await service.FindUsers("query", CancellationToken.None)).ToArray();

        Assert.Collection(results,
            result =>
            {
                Assert.Equal("internal-1", result.UserName);
                Assert.Equal("duplicate@example.org", result.Email);
            },
            result =>
            {
                Assert.Equal("internal-2", result.UserName);
                Assert.Equal("internal2@example.org", result.Email);
            },
            result =>
            {
                Assert.Equal("external-unique", result.UserName);
                Assert.Equal("unique@example.org", result.Email);
            });
    }
}