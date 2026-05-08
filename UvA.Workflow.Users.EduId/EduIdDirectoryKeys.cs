namespace UvA.Workflow.Users.EduId;

public static class EduIdDirectoryKeys
{
    public const string ProviderKey = "eduid";
    public const string SourceKey = "eduid";

    public static bool IsEduId(string? providerKey)
        => UserProviderKeys.AreEqual(providerKey, ProviderKey);
}