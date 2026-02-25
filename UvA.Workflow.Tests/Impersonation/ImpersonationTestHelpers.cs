using MongoDB.Bson;
using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Events;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Impersonation;

/// <summary>
/// Shared constants and factory methods for impersonation tests.
/// </summary>
internal static class ImpersonationTestHelpers
{
    /// <summary>
    /// Default signing key used across impersonation tests (min 64 chars for HMAC-SHA512).
    /// </summary>
    public const string SigningKey =
        "9KPKKaPrV8smLNN2WtANtb8bXTYlVV4h9KPKKaPrV8smLNN2WtANtb8bXTYlVV4h9KPKKaPrV8smLNN2WtANtb8bXTYlVV4h";

    /// <summary>
    /// Alternate key used only in tests that verify token validation with wrong key.
    /// </summary>
    public const string AlternateSigningKey =
        "5sGHQvVvPjrf0AH5LsQY2z6raH9qiXx25sGHQvVvPjrf0AH5LsQY2z6raH9qiXx25sGHQvVvPjrf0AH5LsQY2z6raH9qiXx2";

    public static ModelService CreateModelService()
        => new(new ModelParser(new FileSystemProvider("../../../../Examples/Projects")));

    public static WorkflowInstance CreateProjectInstance(string? id = null) => new()
    {
        Id = id ?? ObjectId.GenerateNewId().ToString(),
        WorkflowDefinition = "Project",
        CurrentStep = "Upload",
        Properties = new Dictionary<string, BsonValue>(),
        Events = new Dictionary<string, InstanceEvent>()
    };
}