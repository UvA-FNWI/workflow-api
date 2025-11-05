using System.Text;
using System.Text.Json;

namespace UvA.Workflow.Tools;

public static class DataExtensions
{
    public static void ForEach<T>(this IEnumerable<T> collection, Action<T> action)
    {
        foreach (var el in collection)
            action(el);
    }

    public static string Coalesce(this string? left, string right)
        => string.IsNullOrEmpty(left) ? right : left;

    /// <summary>
    /// Converts a list of objects to a separated string
    /// </summary>
    /// <typeparam name="T">The type of the objects</typeparam>
    /// <param name="list">The list to convert</param>
    /// <param name="displayFunction">A function that gets converts each object to a string</param>
    /// <param name="separator">The separator to use between the objects</param>
    /// <param name="word">An optional word to use for the last object, e.g. 'and'</param>
    /// <returns></returns>
    public static string ToSeparatedString<T>(this IEnumerable<T> list, Func<T, string?>? displayFunction = null,
        string separator = ", ", string? word = null)
    {
        // If no function is specified, just call the ToString method on each object
        if (displayFunction == null)
            displayFunction = d => d?.ToString() ?? "";

        var builder = new StringBuilder();
        var count = 0;
        foreach (var l in list)
        {
            count++;

            // Append the object
            builder.Append(displayFunction(l));

            // If this is the last object, there is more than one object AND the word parameter is specified, 
            // add the word. Otherwise, add the separator if this is not the last object
            if (word != null && count > 0 && count == list.Count() - 1)
                builder.Append(" " + word + " ");
            else if (count != list.Count())
                builder.Append(separator);
        }

        return builder.ToString();
    }

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static string Serialize<T>(this T obj) => JsonSerializer.Serialize(obj, Options);

    public static string? GetStringValue(this Dictionary<string, BsonValue> dict, string key)
    {
        return dict.TryGetValue(key, out var value) && !value.IsBsonNull
            ? value.AsString
            : null;
    }
}