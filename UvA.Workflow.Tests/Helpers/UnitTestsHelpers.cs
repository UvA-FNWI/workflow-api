using Microsoft.Extensions.Options;
using MongoDB.Bson;
using UvA.Workflow.Entities.Domain;
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
}