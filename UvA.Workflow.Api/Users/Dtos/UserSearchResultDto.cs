namespace UvA.Workflow.Api.Users.Dtos;

public record UserSearchResultDto(
    string UserName,
    string DisplayName,
    string Email,
    string SourceKey,
    Organization? Organization,
    bool IsExternal)
{
    public static UserSearchResultDto Create(UserSearchResult userSearchResult) => new(userSearchResult.UserName,
        userSearchResult.DisplayName,
        userSearchResult.Email,
        userSearchResult.SourceKey,
        userSearchResult.Organization,
        userSearchResult.IsExternal);
}