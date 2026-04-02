using MongoDB.Bson;
using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Users;

namespace UvA.Workflow.Tests.Controllers.Helpers;

/// <summary>
/// Shared constants and factory methods for impersonation tests.
/// </summary>
internal static class ControllerTestsHelpers
{
    public static ModelService CreateModelService()
        => new(new ModelParser(new FileSystemProvider("../../../../Examples/Projects")));

    public static readonly User AdminUser = new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        UserName = "admin"
    };
}