using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Infrastructure.S3;
using UvA.Workflow.Jobs;
using UvA.Workflow.Notifications;
using UvA.Workflow.Users;

namespace UvA.Workflow.Tests.Helpers;

/// <summary>
/// Shared constants and factory methods for all unit tests.
/// </summary>
internal static class UnitTestsHelpers
{
    public static ModelParser CreateModelParser() => new(new FileSystemProvider("../../../../Examples/Projects"));

    public static readonly User AdminUser = new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        UserName = "admin"
    };

    public static MailBuilder CreateMailBuilder(
        IMailLayoutResolver layoutResolver,
        IConfiguration? configuration = null,
        string workerGroup = "test",
        string environmentName = "Production")
    {
        configuration ??= new Mock<IConfiguration>().Object;
        var workerOptions = Options.Create(new WorkerOptions { WorkerGroup = workerGroup });
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(environmentName);
        return new MailBuilder(layoutResolver, configuration, workerOptions, env.Object);
    }

    public sealed class TestOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    public static TestOptionsMonitor<S3Config> TestS3Config => new(
        new S3Config
        {
            ServiceUrl = "http://serviceurl",
            AuthenticationRegion = "EU",
            AccessKey = "zh7F5ZZxmchb3We49nGVMhESZhRtxhWuZhQCDSQak5M", // Dummy AccessKey
            SecretKey = "LaIhdtuPhgkbczwo9ZcDkFI5E6Cdn7QoN30nP3LUQgM", // Dummy SecretKey
            SigningKey = "criXzMbgewG6VC1ebBcmSN92bl496oc0xNOaM6cCS7e" // Dummy SigningKey
        });
}