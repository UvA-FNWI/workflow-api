using System.Net;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Moq;
using UvA.Workflow.Users;
using UvA.Workflow.Users.DataNose;

namespace UvA.Workflow.Tests.Users;

public class DataNoseApiClientTests
{
    private static IHttpClientFactory CreateFactory(string jsonResponse, Action<HttpRequestMessage>? inspect = null)
    {
        var client = new HttpClient(new StubHttpMessageHandler(jsonResponse, inspect))
        {
            BaseAddress = new Uri("https://example.test/")
        };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(DataNoseApiClient.Name)).Returns(client);
        return factory.Object;
    }

    [Fact]
    public async Task SearchPeople_MapsDepartmentCodeToOrganization()
    {
        // camelCase mirrors the real DataNose response shape.
        const string json = """
                            [{"employeeUvAnetID":"emp1","studentID":null,"email":"emp@uva.nl","department":"FNWI/CoI","fullName":"Emp One"}]
                            """;
        var client = new DataNoseApiClient(CreateFactory(json));

        var user = Assert.Single(await client.SearchPeople("emp"));

        Assert.NotNull(user.Organization);
        Assert.Equal("FNWI/CoI", user.Organization.Name);
    }

    [Fact]
    public async Task SearchPeople_LeavesOrganizationNull_WhenDepartmentMissing()
    {
        // Students typically have a null department; the fallback is applied later by the search source.
        const string json = """
                            [{"employeeUvAnetID":null,"studentID":"123","email":"stud@uva.nl","department":null,"fullName":"Stud One"}]
                            """;
        var client = new DataNoseApiClient(CreateFactory(json));

        var user = Assert.Single(await client.SearchPeople("stud"));

        Assert.Null(user.Organization);
    }

    [Theory]
    [InlineData(true, false, new[] { "emp1" })]
    [InlineData(false, true, new[] { "0000123" })]
    [InlineData(true, true, new[] { "emp1", "0000123" })]
    public async Task SearchPeople_MapsOnlyRequestedLoginIdentities(bool includeEmployees, bool includeStudents,
        string[] expected)
    {
        const string json = """
                            [
                              {"employeeUvAnetID":"emp1","studentID":"123","isActiveEmployee":true,"isActiveStudent":true,"email":"dual@uva.nl","fullName":"Dual Identity"},
                              {"studentID":null,"isActiveStudent":true,"email":"no-sis-id@uva.nl","fullName":"No SIS ID"}
                            ]
                            """;
        var client = new DataNoseApiClient(CreateFactory(json, request =>
        {
            var parameters = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            Assert.Equal(includeEmployees ? "true" : "false", parameters["IncludeEmployees"].ToString());
            Assert.Equal(includeStudents ? "true" : "false", parameters["IncludeStudents"].ToString());
            Assert.Equal(includeEmployees ? "true" : "false", parameters["IncludeAMC"].ToString());
        }));

        var users = await client.SearchPeople("example", scope: new UserSearchScope(includeEmployees, includeStudents));

        Assert.Equal(expected, users.Select(user => user.UserName));
    }

    private sealed class StubHttpMessageHandler(string json, Action<HttpRequestMessage>? inspect) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            inspect?.Invoke(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}