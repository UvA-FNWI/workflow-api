using UvA.Workflow.Users;

namespace UvA.Workflow.WorkflowModel;

public abstract class UserSourceDefinition
{
    public static IReadOnlyDictionary<string, Type> KnownTypes { get; } = new Dictionary<string, Type>
    {
        ["Property"] = typeof(PropertyUserSource)
    };

    public string Type { get; set; } = null!;
}

public abstract class ServiceUserSource : UserSourceDefinition
{
    public const string TypeName = "Service";

    public static IReadOnlyDictionary<string, Type> KnownServices { get; } = new Dictionary<string, Type>
    {
        ["DataNose"] = typeof(DataNoseUserSource)
    };

    /// <summary>The service to search for users.</summary>
    public string Name { get; set; } = null!;
}

/// <summary>Searches the existing directory.</summary>
public sealed class DataNoseUserSource : ServiceUserSource
{
    /// <summary>Limits the directory search to the specified user groups.</summary>
    public DataNoseSearchOptions? SearchOptions { get; set; }
}

/// <summary>Offers users stored in a workflow property.</summary>
public sealed class PropertyUserSource : UserSourceDefinition
{
    /// <summary>Path to a User or [User] property.</summary>
    public string Property { get; set; } = null!;
}

public sealed class DataNoseSearchOptions
{
    /// <summary>User groups to include in the directory search.</summary>
    public UserSearchGroup[] Include { get; set; } = [];

    public UserSearchScope ToScope() => new(
        Include.Contains(UserSearchGroup.Employees), Include.Contains(UserSearchGroup.Students));
}

public enum UserSearchGroup
{
    Employees,
    Students
}