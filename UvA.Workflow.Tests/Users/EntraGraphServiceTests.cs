using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UvA.Workflow.Users;
using UvA.Workflow.Users.EntraId;
using UvA.Workflow.Users.EntraId.Infrastructure;

namespace UvA.Workflow.Tests.Users;

public class EntraGraphServiceTests
{
    [Fact]
    public async Task SearchUsers_ReadsSelectedFields_AndSkipsUsersWithoutAName()
    {
        const string json = """
                            {
                              "value": [
                                {
                                  "accountEnabled": true,
                                  "userType": "Member",
                                  "displayName": "Emp One",
                                  "userPrincipalName": "emp.one@uva.nl",
                                  "mail": "emp1@uva.nl",
                                  "onPremisesSamAccountName": "emp1",
                                  "onPremisesExtensionAttributes": {
                                    "extensionAttribute2": "valid",
                                    "extensionAttribute4": "employee"
                                  }
                                },
                                {
                                  "userPrincipalName": "noname@uva.nl",
                                  "mail": "noname@uva.nl",
                                  "onPremisesSamAccountName": "noname"
                                }
                              ]
                            }
                            """;
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, json));
        var service = Create(handler);

        var result = Assert.Single(await service.SearchUsersAsync("emp one"));

        Assert.Equal("emp1", result.SamAccountName);
        Assert.Equal("emp1@uva.nl", result.Mail);
        var request = Assert.Single(handler.Requests);
        var uri = Uri.UnescapeDataString(request.RequestUri!.ToString());
        Assert.Contains("$search=", uri, StringComparison.Ordinal);
        Assert.Contains("extensionAttribute4 eq 'employee'", uri, StringComparison.Ordinal);
        Assert.Contains("extensionAttribute2 eq 'valid'", uri, StringComparison.Ordinal);
        Assert.Contains("accountEnabled eq true", uri, StringComparison.Ordinal);
        Assert.Contains("userType eq 'Member'", uri, StringComparison.Ordinal);
        Assert.Contains("$top=10", uri, StringComparison.Ordinal);
        Assert.DoesNotContain("startsWith(onPremisesSamAccountName", uri, StringComparison.Ordinal);
        Assert.All(handler.Requests, sent =>
        {
            Assert.Contains("ConsistencyLevel", sent.Headers.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("mailNickname", sent.RequestUri!.ToString(), StringComparison.Ordinal);
            Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
        });
    }

    [Fact]
    public async Task SearchUsers_MergesASamMatch_ThatTheNameSearchMissed()
    {
        const string byName = """
                              {"value":[{"accountEnabled":true,"userType":"Member","displayName":"By Name","userPrincipalName":"name@uva.nl","mail":"name@uva.nl","onPremisesSamAccountName":"name1","onPremisesExtensionAttributes":{"extensionAttribute2":"valid","extensionAttribute4":"employee"}}]}
                              """;
        const string bySam = """
                             {"value":[{"accountEnabled":true,"userType":"Member","displayName":"By Sam","userPrincipalName":"sam@uva.nl","mail":"sam@uva.nl","onPremisesSamAccountName":"emp1","onPremisesExtensionAttributes":{"extensionAttribute2":"valid","extensionAttribute4":"employee"}}]}
                             """;
        var handler = new StubHandler(request =>
        {
            var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
            var json = url.Contains("startsWith(onPremisesSamAccountName", StringComparison.Ordinal) ? bySam : byName;
            return Json(HttpStatusCode.OK, json);
        });
        var service = Create(handler);

        var results = await service.SearchUsersAsync("emp1");

        Assert.Equal(["name@uva.nl", "sam@uva.nl"], results.Select(user => user.UserPrincipalName).ToArray());
    }

    [Fact]
    public async Task SearchUsers_KeepsNameResults_WhenTheSamFilterFails()
    {
        const string byName = """
                              {"value":[{"accountEnabled":true,"userType":"Member","displayName":"By Name","userPrincipalName":"name@uva.nl","mail":"name@uva.nl","onPremisesSamAccountName":"name1","onPremisesExtensionAttributes":{"extensionAttribute2":"valid","extensionAttribute4":"employee"}}]}
                              """;
        var handler = new StubHandler(request =>
        {
            var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
            return url.Contains("startsWith(onPremisesSamAccountName", StringComparison.Ordinal)
                ? Json(HttpStatusCode.BadRequest, """{"error":"nope"}""")
                : Json(HttpStatusCode.OK, byName);
        });
        var service = Create(handler);

        var result = Assert.Single(await service.SearchUsersAsync("name"));

        Assert.Equal("name@uva.nl", result.UserPrincipalName);
    }

    [Fact]
    public async Task SearchUsers_EscapesQuotesInTheSamFilter()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"value":[]}"""));
        var service = Create(handler);

        Assert.Empty(await service.SearchUsersAsync("O'Brien"));

        var filter = handler.Requests.Select(request => Uri.UnescapeDataString(request.RequestUri!.ToString()))
            .Single(url => url.Contains("startsWith(onPremisesSamAccountName", StringComparison.Ordinal));
        Assert.Contains("startsWith(onPremisesSamAccountName,'O''Brien')", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadOrganization_JoinsInstituteAndDepartments_StrippingSemicolons()
    {
        using var doc = JsonDocument.Parse("""
                                           {"onPremisesExtensionAttributes":{
                                             "extensionAttribute5":";FNWI;",
                                             "extensionAttribute6":";Dir&Dnst/FNW;",
                                             "extensionAttribute7":";Secr. FNWI;"}}
                                           """);

        Assert.Equal("FNWI | Dir&Dnst/FNW | Secr. FNWI", EntraGraphService.ReadOrganization(doc.RootElement));
    }

    [Fact]
    public void ReadOrganization_SkipsEmptyAttributes()
    {
        using var doc = JsonDocument.Parse("""
                                           {"onPremisesExtensionAttributes":{
                                             "extensionAttribute5":null,
                                             "extensionAttribute6":";Dir&Dnst/FNW;",
                                             "extensionAttribute7":";;"}}
                                           """);

        Assert.Equal("Dir&Dnst/FNW", EntraGraphService.ReadOrganization(doc.RootElement));
    }

    [Fact]
    public void ReadOrganization_ReturnsNull_WhenNoAttributesArePresent()
    {
        using var doc = JsonDocument.Parse("""{"displayName":"Emp"}""");

        Assert.Null(EntraGraphService.ReadOrganization(doc.RootElement));
    }

    [Fact]
    public async Task SearchUsers_CarriesTheOrganization_FromExtensionAttributes()
    {
        const string json = """
                            {"value":[{"accountEnabled":true,"userType":"Member","displayName":"Emp One","userPrincipalName":"emp.one@uva.nl",
                              "mail":"emp1@uva.nl","onPremisesSamAccountName":"emp1",
                              "onPremisesExtensionAttributes":{"extensionAttribute2":"valid","extensionAttribute4":"employee","extensionAttribute5":";FNWI;"}}]}
                            """;
        var service = Create(new StubHandler(_ => Json(HttpStatusCode.OK, json)));

        var result = Assert.Single(await service.SearchUsersAsync("emp one"));

        Assert.Equal("FNWI", result.Organization);
    }

    [Fact]
    public async Task SearchUsers_ReturnsEmpty_OnGraphError_WithoutLoggingTheBody()
    {
        var logger = new ListLogger<EntraGraphService>();
        var handler = new StubHandler(_ =>
        {
            var response = Json(HttpStatusCode.Unauthorized, """{"error":"query-should-stay-out-of-logs"}""");
            response.Headers.TryAddWithoutValidation("request-id", "req-1");
            return response;
        });
        var service = Create(handler, logger: logger);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => service.SearchUsersAsync("emp"));

        var written = string.Join('\n', logger.Messages);
        Assert.Contains("401", written, StringComparison.Ordinal);
        Assert.Contains("req-1", written, StringComparison.Ordinal);
        Assert.DoesNotContain("query-should-stay-out-of-logs", written, StringComparison.Ordinal);
        Assert.DoesNotContain("emp", written, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task SearchUsers_ReturnsEmpty_WhenGraphDoesNotSucceed(HttpStatusCode status)
    {
        var service = Create(new StubHandler(_ => Json(status, "{}")));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => service.SearchUsersAsync("emp"));
    }

    [Fact]
    public async Task SearchUsers_Throws_WhenJsonIsMalformed()
    {
        var service = Create(new StubHandler(_ => Json(HttpStatusCode.OK, "{")));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => service.SearchUsersAsync("emp"));
    }

    [Fact]
    public async Task SearchUsers_Throws_WhenTokenRequestFails()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Graph was called"));
        var service = Create(handler, new TokenSource(new InvalidOperationException("token down")));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => service.SearchUsersAsync("emp"));
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task SearchUsers_OmitsStudentsAndDisabledAccounts()
    {
        const string json = """
                            {"value":[
                              {"accountEnabled":true,"userType":"Member","displayName":"Emp","userPrincipalName":"emp@uva.nl","mail":"emp@uva.nl","onPremisesSamAccountName":"emp1","onPremisesExtensionAttributes":{"extensionAttribute2":"valid","extensionAttribute4":"employee"}},
                              {"accountEnabled":true,"userType":"Member","displayName":"Student","userPrincipalName":"stud@uva.nl","mail":"stud@uva.nl","onPremisesSamAccountName":"1234567","onPremisesExtensionAttributes":{"extensionAttribute2":"valid","extensionAttribute4":"student"}},
                              {"accountEnabled":false,"displayName":"Gone","userPrincipalName":"gone@uva.nl","mail":"gone@uva.nl","onPremisesSamAccountName":"gone1","onPremisesExtensionAttributes":{"extensionAttribute2":"valid","extensionAttribute4":"employee"}},
                              {"accountEnabled":true,"userType":"Member","displayName":"Old","userPrincipalName":"old@uva.nl","mail":"old@uva.nl","onPremisesSamAccountName":"old1","onPremisesExtensionAttributes":{"extensionAttribute2":"invalid","extensionAttribute4":"employee"}},
                              {"accountEnabled":true,"userType":"Guest","displayName":"Guest","userPrincipalName":"guest_hva.nl#EXT#@tenant.onmicrosoft.com","mail":"guest@hva.nl","onPremisesSamAccountName":"guest1","onPremisesExtensionAttributes":{"extensionAttribute2":"valid","extensionAttribute4":"employee"}}
                            ]}
                            """;
        var service = Create(new StubHandler(_ => Json(HttpStatusCode.OK, json)));

        var result = Assert.Single(await service.SearchUsersAsync("people"));

        Assert.Equal("emp1", result.SamAccountName);
    }

    [Fact]
    public async Task SearchUsers_QueriesEveryTenant_AndMergesMembers()
    {
        const string uva = """
                           {"value":[{"accountEnabled":true,"userType":"Member","displayName":"UvA Emp","userPrincipalName":"uva@uva.nl","mail":"uva@uva.nl","onPremisesSamAccountName":"uva1","onPremisesExtensionAttributes":{"extensionAttribute2":"valid","extensionAttribute4":"employee"}}]}
                           """;
        const string hva = """
                           {"value":[{"accountEnabled":true,"userType":"Member","displayName":"HvA Emp","userPrincipalName":"hva@hva.nl","mail":"hva@hva.nl","onPremisesSamAccountName":"hva1","onPremisesExtensionAttributes":{"extensionAttribute2":"valid","extensionAttribute4":"employee"}}]}
                           """;
        var handler = new StubHandler(request =>
        {
            var token = request.Headers.Authorization!.Parameter;
            return Json(HttpStatusCode.OK, token == "token-hva" ? hva : uva);
        });
        var service = Create(handler, new TokenSource(null, "uva", "hva"));

        var results = await service.SearchUsersAsync("emp one");

        Assert.Equal(["uva@uva.nl", "hva@hva.nl"], results.Select(user => user.UserPrincipalName).ToArray());
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SearchUsers_ReturnsTheOtherTenant_WhenOneTenantFails()
    {
        const string hva = """
                           {"value":[{"accountEnabled":true,"userType":"Member","displayName":"HvA Emp","userPrincipalName":"hva@hva.nl","mail":"hva@hva.nl","onPremisesSamAccountName":"hva1","onPremisesExtensionAttributes":{"extensionAttribute2":"valid","extensionAttribute4":"employee"}}]}
                           """;
        var handler = new StubHandler(request =>
            request.Headers.Authorization!.Parameter == "token-hva"
                ? Json(HttpStatusCode.OK, hva)
                : Json(HttpStatusCode.ServiceUnavailable, "{}"));
        var service = Create(handler, new TokenSource(null, "uva", "hva"));

        var result = Assert.Single(await service.SearchUsersAsync("emp one"));

        Assert.Equal("hva@hva.nl", result.UserPrincipalName);
    }

    [Fact]
    public async Task GetUserBySamAccountName_ReturnsTheSingleTenantHit()
    {
        const string hit = """
                           {"@odata.count":1,"value":[{"userType":"Member","displayName":"HvA Emp","userPrincipalName":"hva@hva.nl","mail":"hva@hva.nl","onPremisesSamAccountName":"hva1"}]}
                           """;
        var handler = new StubHandler(request =>
            request.Headers.Authorization!.Parameter == "token-hva"
                ? Json(HttpStatusCode.OK, hit)
                : Json(HttpStatusCode.OK, """{"@odata.count":0,"value":[]}"""));
        var service = Create(handler, new TokenSource(null, "uva", "hva"));

        var result = await service.GetUserBySamAccountNameAsync("hva1");

        Assert.Equal("hva1", result!.SamAccountName);
    }

    [Fact]
    public async Task GetUserBySamAccountName_ReturnsNull_WhenTwoTenantsMatch()
    {
        const string hit = """
                           {"@odata.count":1,"value":[{"userType":"Member","displayName":"Emp","userPrincipalName":"emp@uva.nl","mail":"emp@uva.nl","onPremisesSamAccountName":"emp1"}]}
                           """;
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, hit));
        var service = Create(handler, new TokenSource(null, "uva", "hva"));

        Assert.Null(await service.GetUserBySamAccountNameAsync("emp1"));
    }

    [Fact]
    public async Task SearchUsers_DoesNotCallGraph_ForAShortQuery()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Graph was called"));
        var service = Create(handler);

        Assert.Empty(await service.SearchUsersAsync("a"));
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task SearchUsers_PropagatesCancellation()
    {
        var service = Create(new StubHandler(_ => throw new OperationCanceledException()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SearchUsersAsync("emp"));
    }

    [Fact]
    public async Task DisposeWhenDone_DisposesDocumentThatCompletesLater()
    {
        var pending = new TaskCompletionSource<JsonDocument?>();
        var doc = JsonDocument.Parse("""{"value":[]}""");

        EntraGraphService.DisposeWhenDone(pending.Task);
        pending.SetResult(doc);
        await pending.Task;

        Assert.Throws<ObjectDisposedException>(() => doc.RootElement.GetRawText());
    }

    [Fact]
    public async Task GetUserBySamAccountName_UsesExactAdvancedFilter()
    {
        const string json = """
                            {
                              "@odata.count": 1,
                              "value": [
                                {
                                  "userType": "Member",
                                  "displayName": "Emp One",
                                  "userPrincipalName": "emp.one@uva.nl",
                                  "mail": "emp1@uva.nl",
                                  "onPremisesSamAccountName": "O'Brien"
                                }
                              ]
                            }
                            """;
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, json));
        var service = Create(handler);

        var result = await service.GetUserBySamAccountNameAsync(" O'Brien ");

        Assert.NotNull(result);
        Assert.Equal("O'Brien", result.SamAccountName);
        var query = Uri.UnescapeDataString(handler.LastRequest!.RequestUri!.Query);
        Assert.Contains("onPremisesSamAccountName eq 'O''Brien'", query, StringComparison.Ordinal);
        Assert.Contains("userType eq 'Member'", query, StringComparison.Ordinal);
        Assert.Contains("$count=true", query, StringComparison.Ordinal);
        Assert.Contains("ConsistencyLevel", handler.LastRequest.Headers.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUserBySamAccountName_ReturnsNull_WhenNobodyMatches()
    {
        var service = Create(new StubHandler(_ => Json(HttpStatusCode.OK, """{"@odata.count":0,"value":[]}""")));

        Assert.Null(await service.GetUserBySamAccountNameAsync("missing"));
    }

    [Fact]
    public async Task GetUserBySamAccountName_ReturnsNull_WhenSeveralUsersMatch()
    {
        const string json = """
                            {
                              "@odata.count": 2,
                              "value": [
                                {
                                  "userType": "Member",
                                  "displayName": "A",
                                  "userPrincipalName": "a@uva.nl",
                                  "mail": "a@uva.nl",
                                  "onPremisesSamAccountName": "emp1"
                                },
                                {
                                  "userType": "Member",
                                  "displayName": "B",
                                  "userPrincipalName": "b@uva.nl",
                                  "mail": "b@uva.nl",
                                  "onPremisesSamAccountName": "emp1"
                                }
                              ]
                            }
                            """;
        var service = Create(new StubHandler(_ => Json(HttpStatusCode.OK, json)));

        Assert.Null(await service.GetUserBySamAccountNameAsync("emp1"));
    }

    [Fact]
    public async Task GetUserBySamAccountName_ReturnsNull_WhenThePropertyIsMissing()
    {
        const string json = """
                            {
                              "@odata.count": 1,
                              "value": [
                                {
                                  "displayName": "Emp One",
                                  "userPrincipalName": "emp.one@uva.nl",
                                  "mail": "emp1@uva.nl"
                                }
                              ]
                            }
                            """;
        var service = Create(new StubHandler(_ => Json(HttpStatusCode.OK, json)));

        Assert.Null(await service.GetUserBySamAccountNameAsync("emp1"));
    }

    private static EntraGraphService Create(
        StubHandler handler,
        TokenSource? tokens = null,
        ILogger<EntraGraphService>? logger = null)
        => new(
            tokens ?? new TokenSource(),
            new Factory(new HttpClient(handler)),
            logger ?? NullLogger<EntraGraphService>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class TokenSource(Exception? error = null, params string[] tenantIds) : IEntraAccessTokenSource
    {
        public IReadOnlyList<string> TenantIds { get; } = tenantIds.Length == 0 ? ["tenant"] : tenantIds;

        public Task<string> GetAccessTokenAsync(string tenantId, CancellationToken ct)
            => error == null ? Task.FromResult("token-" + tenantId) : Task.FromException<string>(error);
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}