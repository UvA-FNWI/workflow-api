namespace UvA.Workflow.Users;

public record UserSearchResult(
    string UserName,
    string DisplayName,
    string Email,
    UserSearchSource SearchSource,
    Organization? Organization = null,
    bool IsExternal = false);