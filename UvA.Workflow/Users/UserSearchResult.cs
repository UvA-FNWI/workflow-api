namespace UvA.Workflow.Users;

public record UserSearchResult(
    string UserName,
    string DisplayName,
    string Email,
    UserSearchSource SearchSource,
    Organization? Organization = null);

public record Organization(string Id, string Name);