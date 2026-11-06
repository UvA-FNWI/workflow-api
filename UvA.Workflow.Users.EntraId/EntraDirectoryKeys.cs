namespace UvA.Workflow.Users.EntraId;

public static class EntraDirectoryKeys
{
    public const string ProviderKey = UserProviderKeys.Internal;
    public const string SourceKey = "entra-id";

    public static bool IsSelected(string? userProvider)
    {
        var value = userProvider?.Trim();
        return value != null &&
               (value.Equals("entra", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("entra-id", StringComparison.OrdinalIgnoreCase));
    }
}