namespace UvA.Workflow.Users;

public record UserSearchResult(
    string UserName,
    string DisplayName,
    string Email,
    string SourceKey,
    [property: JsonIgnore] string ProviderKey = UserProviderKeys.Internal,
    Organization? Organization = null)
{
    public bool IsExternal => UserProviderKeys.IsExternal(ProviderKey);
}