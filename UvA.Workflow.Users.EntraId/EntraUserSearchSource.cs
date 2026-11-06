using UvA.Workflow.Users.EntraId.Infrastructure;

namespace UvA.Workflow.Users.EntraId;

public class EntraUserSearchSource(IEntraGraphService graph) : IUserSearchSource
{
    public async Task<IEnumerable<UserSearchResult>> FindUsers(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var users = await graph.SearchUsersAsync(query.Trim(), ct);
        return users.Select(ToSearchResult).ToArray();
    }

    private static UserSearchResult ToSearchResult(EntraUserInfo user)
    {
        var email = string.IsNullOrWhiteSpace(user.Mail) ? user.UserPrincipalName : user.Mail;
        return new UserSearchResult(
            DirectoryUserName(user),
            user.DisplayName,
            email,
            EntraDirectoryKeys.SourceKey);
    }

    /// <summary>
    /// SurfConext stores the uid in <see cref="User.UserName"/>. Prefer on-premises SAM / mail
    /// nickname so a picker hit matches the user created at login. Guests keep the full UPN.
    /// </summary>
    internal static string DirectoryUserName(EntraUserInfo user)
    {
        if (!string.IsNullOrWhiteSpace(user.SamAccountName))
            return user.SamAccountName.Trim();

        if (!string.IsNullOrWhiteSpace(user.MailNickname) && !user.MailNickname.Contains('@'))
            return user.MailNickname.Trim();

        var upn = user.UserPrincipalName.Trim();
        if (upn.Contains("#EXT#", StringComparison.OrdinalIgnoreCase))
            return upn;

        var at = upn.IndexOf('@');
        return at > 0 ? upn[..at] : upn;
    }
}