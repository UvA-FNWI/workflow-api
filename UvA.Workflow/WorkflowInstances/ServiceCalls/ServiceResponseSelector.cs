using System.Text.Json;
using UvA.Workflow.Expressions;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.WorkflowInstances.ServiceCalls;

internal static class ServiceResponseSelector
{
    public static JsonElement Select(JsonElement response, ServiceResponseSelection selection,
        ObjectContext inputs)
    {
        var collection = Get(response, selection.CollectionPath);
        if (collection.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Service response requires a result array.");

        var criteria = selection.Match.ToDictionary(pair => pair.Key,
            pair => Template.Create(pair.Value).Apply(inputs));
        if (criteria.Count == 0 || criteria.Any(pair => string.IsNullOrWhiteSpace(pair.Value)))
            throw new InvalidOperationException("Service response selection requires nonempty match values.");

        var matches = collection.EnumerateArray()
            .Where(item => criteria.All(pair =>
            {
                var value = Get(item, pair.Key);
                return value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) &&
                       string.Equals(value.ToString(), pair.Value, StringComparison.Ordinal);
            }))
            .Take(2).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                $"Service response selection requires exactly one matching result; found {(matches.Length == 2 ? "multiple" : "none")}.");

        return matches[0];
    }

    private static JsonElement Get(JsonElement value, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Service response selection requires property paths.");
        foreach (var part in path.Split('.'))
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part, out value))
                return default;
        return value;
    }
}