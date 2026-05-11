namespace UvA.Workflow.Api.Users.Dtos;

public record UserSearchResultDto(
    string UserName,
    string DisplayName,
    string Email,
    string SourceKey,
    string ProviderKey,
    Organization? Organization,
    bool IsExternal)
{
    public static UserSearchResultDto Create(UserSearchResult userSearchResult) => new(userSearchResult.UserName,
        userSearchResult.DisplayName,
        userSearchResult.Email,
        userSearchResult.SourceKey,
        userSearchResult.ProviderKey,
        userSearchResult.Organization,
        userSearchResult.IsExternal);
}