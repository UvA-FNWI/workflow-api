using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UvA.Workflow.Users.EntraId;

namespace UvA.Workflow.Tests.Users;

public class EntraGraphOptionsTests
{
    [Fact]
    public void Validate_RejectsATimeoutThatCancelsRetries()
    {
        var options = ValidOptions();
        options.RequestTimeoutSeconds = EntraGraphOptions.AttemptTimeoutSeconds;

        var error = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("RequestTimeoutSeconds", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddWorkflowEntraIdUsers_FailsAtRegistration_WhenConfigurationIsMissing()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        var error = Assert.Throws<InvalidOperationException>(() => services.AddWorkflowEntraIdUsers(config));

        Assert.Contains("EntraId:ClientId", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddWorkflowEntraIdUsers_LeavesHttpTimeoutToTheResilienceHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkflowEntraIdUsers(ValidConfig());

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ServiceCollectionExtensions.HttpClientName);

        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Fact]
    public void ResolvedTenants_UsesTheAuthorityTenant_ThenExtraTenants()
    {
        var options = ValidOptions();
        options.Tenants =
        [
            new EntraTenantOptions { TenantId = "hva" },
            new EntraTenantOptions { TenantId = "tenant" }
        ];

        var tenants = options.ResolvedTenants();

        Assert.Equal("tenant", tenants[0].TenantId);
        Assert.Equal("hva", tenants[1].TenantId);
        Assert.Equal(2, tenants.Count);
        Assert.Equal("https://login.microsoftonline.com/hva/", tenants[1].Authority);
    }

    [Fact]
    public void AddWorkflowEntraIdUsers_BindsTenantIdsFromAStringArray()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EntraId:ClientId"] = "client",
            ["EntraId:ClientSecret"] = "secret",
            ["EntraId:Authority"] = "https://login.microsoftonline.com/uva",
            ["EntraId:Tenants:0"] = "hva"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkflowEntraIdUsers(config);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<EntraGraphOptions>();

        Assert.Equal(["uva", "hva"], options.ResolvedTenants().Select(tenant => tenant.TenantId));
    }

    [Fact]
    public void Validate_AcceptsTenantsWithoutAnAuthority()
    {
        var options = ValidOptions();
        options.Authority = null;
        options.Tenants = [new EntraTenantOptions { TenantId = "hva" }];

        options.Validate();

        Assert.Equal("hva", Assert.Single(options.ResolvedTenants()).TenantId);
    }

    [Fact]
    public void Validate_RejectsAnAuthorityThatIsNotATenant()
    {
        var options = ValidOptions();
        options.Authority = "https://login.microsoftonline.com/organizations";

        var error = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("EntraId:Authority", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ReportsTheSameFailureAsValidate()
    {
        var result = new EntraGraphOptionsValidator().Validate(null, new EntraGraphOptions());

        Assert.True(result.Failed);
        Assert.Contains("EntraId:ClientId", result.FailureMessage, StringComparison.Ordinal);
    }

    private static EntraGraphOptions ValidOptions() => new()
    {
        ClientId = "client",
        ClientSecret = "secret",
        Authority = "https://login.microsoftonline.com/tenant"
    };

    private static IConfiguration ValidConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EntraId:ClientId"] = "client",
            ["EntraId:ClientSecret"] = "secret",
            ["EntraId:Authority"] = "https://login.microsoftonline.com/tenant"
        })
        .Build();
}