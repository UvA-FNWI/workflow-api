using Microsoft.Extensions.Options;
using MongoDB.Bson;
using UvA.Workflow.Infrastructure.S3;
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