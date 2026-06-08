using System.Net;
using System.Text;
using Moq;
using UvA.Workflow.Users.DataNose;

namespace UvA.Workflow.Tests.Users;

public class DataNoseApiClientTests
{
    private static IHttpClientFactory CreateFactory(string jsonResponse)
    {
        var client = new HttpClient(new StubHttpMessageHandler(jsonResponse))
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
        Assert.Equal("FNWI/CoI", user.Organization!.Id);
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

    private sealed class StubHttpMessageHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }
}