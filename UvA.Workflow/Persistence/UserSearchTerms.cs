namespace UvA.Workflow.Persistence;

public static class UserSearchTerms
{
    public static string Normalize(string value) => string.Join(' ', SplitWords(value)).ToUpperInvariant();

    public static string[] Create(User user)
    {
        var name = SplitWords(user.DisplayName);
        // Starting at each name word lets people search by surname or full name.
        return Enumerable.Range(0, name.Length)
            .Select(index => string.Join(' ', name[index..]).ToUpperInvariant())
            .Append(Normalize(user.UserName))
            .Append(Normalize(user.Email))
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] SplitWords(string? value) =>
        value?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];
}