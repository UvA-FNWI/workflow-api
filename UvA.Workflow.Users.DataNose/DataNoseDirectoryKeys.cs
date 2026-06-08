namespace UvA.Workflow.Users.DataNose;

public static class DataNoseDirectoryKeys
{
    public const string ProviderKey = UserProviderKeys.Internal;
    public const string SourceKey = "datanose";

    /// <summary>
    /// DataNose's super-admin role. Mirrors <c>SystemRole.SystemAdmin</c> in the DataNose WebAPI and
    /// arrives here verbatim as a role name from the GetRolesForUser endpoint.
    /// </summary>
    public const string SuperAdminRoleName = "SystemAdmin";
}