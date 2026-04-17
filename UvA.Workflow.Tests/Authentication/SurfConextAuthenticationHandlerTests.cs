using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Api.Authentication.SurfConext;
using UvA.Workflow.Users;
using UvA.Workflow.Users.EduId;

namespace UvA.Workflow.Tests.Authentication;

public class SurfConextAuthenticationHandlerTests
{
    [Fact]
    public async Task AuthenticateAsync_EduIdActiveUser_Succeeds()
    {
        var userServiceMock = new Mock<IUserService>();
        var eduIdUserServiceMock = new Mock<IEduIdUserService>();
        eduIdUserServiceMock.Setup(s => s.ResolveAuthenticatedUser("eduid-123",
                "External User",
                "external@example.org",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                UserName = "eduid-123",
                Email = "external@example.org",
                ProviderKey = EduIdDirectoryKeys.ProviderKey,
                IsActive = true
            });

        var (handler, _) = CreateHandler(CreateIntrospectionResponse(
                active: true,
                uid: "eduid-123",
                email: "external@example.org",
                name: "External User",
                authority: "https://login.test.eduid.nl"),
            userServiceMock.Object,
            eduIdUserServiceMock.Object);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("eduid-123", result.Principal?.Identity?.Name);
        Assert.Equal("eduid-123", result.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        userServiceMock.Verify(s => s.AddOrUpdateUser(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AuthenticateAsync_UsesCache_DoesNotProvisionUserAgain()
    {
        var userServiceMock = new Mock<IUserService>(MockBehavior.Strict);
        var eduIdUserServiceMock = new Mock<IEduIdUserService>();
        eduIdUserServiceMock.Setup(s => s.ResolveAuthenticatedUser("eduid-123",
                "External User",
                "external@example.org",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                UserName = "eduid-123",
                Email = "external@example.org",
                ProviderKey = EduIdDirectoryKeys.ProviderKey,
                IsActive = true
            });

        var (handler, _) = CreateHandler(CreateIntrospectionResponse(
                active: true,
                uid: "eduid-123",
                email: "external@example.org",
                name: "External User",
                authority: "https://login.test.eduid.nl"),
            userServiceMock.Object,
            eduIdUserServiceMock.Object);

        var firstResult = await handler.AuthenticateAsync();
        var secondResult = await handler.AuthenticateAsync();

        Assert.True(firstResult.Succeeded);
        Assert.True(secondResult.Succeeded);
        eduIdUserServiceMock.Verify(s => s.ResolveAuthenticatedUser("eduid-123",
            "External User",
            "external@example.org",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_NonEduId_UsesRegularUserProvisioning()
    {
        var userServiceMock = new Mock<IUserService>();
        var eduIdUserServiceMock = new Mock<IEduIdUserService>();
        userServiceMock.Setup(s => s.AddOrUpdateUser("jdoe",
                "Jane Doe",
                "jane.doe@uva.nl",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                UserName = "jdoe",
                Email = "jane.doe@uva.nl",
                ProviderKey = UserProviderKeys.Internal,
                IsActive = true
            });

        var (handler, _) = CreateHandler(CreateIntrospectionResponse(
                active: true,
                uid: "jdoe",
                email: "jane.doe@uva.nl",
                name: "Jane Doe",
                authority: "https://idp.uva.nl"),
            userServiceMock.Object,
            eduIdUserServiceMock.Object);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        userServiceMock.VerifyAll();
        eduIdUserServiceMock.Verify(s => s.ResolveAuthenticatedUser(It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AuthenticateAsync_InactiveToken_FailsWithoutProvisioning()
    {
        var userServiceMock = new Mock<IUserService>(MockBehavior.Strict);
        var eduIdUserServiceMock = new Mock<IEduIdUserService>(MockBehavior.Strict);

        var (handler, _) = CreateHandler(CreateIntrospectionResponse(
                active: false,
                uid: "eduid-123",
                email: "external@example.org",
                name: "External User",
                authority: "https://login.test.eduid.nl"),
            userServiceMock.Object,
            eduIdUserServiceMock.Object);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("inactive token", result.Failure?.Message);
    }

    [Fact]
    public async Task ChallengeAsync_UnknownEduIdUser_ReturnsUnauthorizedAndExpectedError()
    {
        var userServiceMock = new Mock<IUserService>();
        var eduIdUserServiceMock = new Mock<IEduIdUserService>();
        eduIdUserServiceMock.Setup(s => s.ResolveAuthenticatedUser(It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var (handler, context) = CreateHandler(CreateIntrospectionResponse(
                active: true,
                uid: "missing-eduid",
                email: "missing@example.org",
                name: "Missing User",
                authority: "https://login.test.eduid.nl"),
            userServiceMock.Object,
            eduIdUserServiceMock.Object);

        var result = await handler.AuthenticateAsync();
        Assert.False(result.Succeeded);

        await handler.ChallengeAsync(new AuthenticationProperties());

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Contains("EduID user is not invited.", body);
    }

    private static (SurfConextAuthenticationHandler Handler, DefaultHttpContext Context) CreateHandler(
        HttpResponseMessage introspectionResponse,
        IUserService userService,
        IEduIdUserService eduIdUserService)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(_ => introspectionResponse))
        {
            BaseAddress = new Uri("https://connect.test.surfconext.nl")
        };
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient(SurfConextAuthenticationHandler.SchemeName))
            .Returns(httpClient);

        var handler = new SurfConextAuthenticationHandler(
            new TestOptionsMonitor<SurfConextOptions>(new SurfConextOptions
            {
                BaseUrl = "https://connect.test.surfconext.nl",
                ClientId = "client-id",
                ClientSecret = "secret"
            }),
            LoggerFactory.Create(_ => { }),
            UrlEncoder.Default,
            httpClientFactoryMock.Object,
            userService,
            eduIdUserService,
            Options.Create(new EduIdOptions { Authority = "https://login.test.eduid.nl" }),
            new MemoryCache(new MemoryCacheOptions()));

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer test-token";
        context.Response.Body = new MemoryStream();
        handler.InitializeAsync(new AuthenticationScheme(SurfConextAuthenticationHandler.SchemeName,
            null,
            typeof(SurfConextAuthenticationHandler)), context).GetAwaiter().GetResult();

        return (handler, context);
    }

    private static HttpResponseMessage CreateIntrospectionResponse(
        bool active,
        string uid,
        string email,
        string name,
        string authority)
    {
        var payload = $$"""
                        {
                          "active": {{active.ToString().ToLowerInvariant()}},
                          "authenticating_authority": "{{authority}}",
                          "client_id": "client-id",
                          "email": "{{email}}",
                          "name": "{{name}}",
                          "uids": ["{{uid}}"]
                        }
                        """;

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    private sealed class TestOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}