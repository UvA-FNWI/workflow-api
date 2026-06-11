namespace UvA.Workflow.Users;

public static class UserProviderKeys
{
    public const string Internal = "internal";
    public const string External = "external";

    public static string Normalize(string? providerKey)
        => string.IsNullOrWhiteSpace(providerKey) ? Internal : providerKey.Trim();

    public static bool AreEqual(string? first, string? second)
        => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    public static bool IsInternal(string? providerKey)
        => AreEqual(providerKey, Internal);

    public static bool IsExternal(string? providerKey)
        => !IsInternal(providerKey);
}