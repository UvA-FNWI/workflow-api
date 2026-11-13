namespace UvA.Workflow.Users;

public static class UserSearchTerms
{
    public static string Normalize(string? value) => string.Join(' ', SplitWords(value)).ToLowerInvariant();

    public static string[] Create(User user)
    {
        var name = SplitWords(user.DisplayName);
        // Start at each word so surname and multi-word prefixes can match.
        return Enumerable.Range(0, name.Length)
            .Select(index => string.Join(' ', name[index..]).ToLowerInvariant())
            .Append(Normalize(user.UserName))
            .Append(Normalize(user.Email))
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] SplitWords(string? value) =>
        value?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];
}
