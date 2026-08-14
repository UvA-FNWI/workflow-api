using MongoDB.Bson.Serialization.Attributes;
using UvA.Workflow.Events;
using UvA.Workflow.Migrations;

namespace UvA.Workflow.WorkflowInstances;

/// <summary>
/// Represents a workflow instance - the core domain entity.
/// Contains business logic for managing workflow state, properties, and events.
/// </summary>
public class WorkflowInstance
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    public string WorkflowDefinition { get; set; } = null!;
    public string? CurrentStep { get; set; }

    public DateTime CreatedOn { get; set; }

    // public List<LogEntry> LogEntries { get; set; } = [];
    public Dictionary<string, BsonValue> Properties { get; set; } = null!;
    public Dictionary<string, InstanceEvent> Events { get; set; } = null!;

    /// <summary>Runtime-only aliases loaded from the global migrations collection.</summary>
    [BsonIgnore]
    public IReadOnlyList<PropertyRenameAlias> PropertyRenameAliases { get; set; } = [];


    public string? ParentId { get; set; }

    public bool HasAnswer(string property)
        => GetProperty(property) is { } value && value != BsonNull.Value;

    public BsonValue? GetProperty(params string?[] parts)
    {
        string[] relevantParts = parts.Where(p => p != null).ToArray()!;
        if (relevantParts.Length == 0) return null;

        return GetRawProperty(relevantParts);
    }

    public void SetProperty(BsonValue? value, params string?[] parts)
    {
        string[] relevantParts = parts.Where(p => p != null).ToArray()!;
        if (relevantParts.Length == 0)
            return;

        var path = string.Join('.', relevantParts);
        foreach (var writePath in WritePaths(path))
            SetRawProperty(value?.DeepClone(), writePath.Split('.'));
    }

    public IReadOnlyList<string> GetPropertyWritePaths(params string?[] parts)
    {
        var relevantParts = parts.Where(part => part != null).ToArray()!;
        return relevantParts.Length == 0
            ? []
            : WritePaths(string.Join('.', relevantParts)).ToArray();
    }

    public void MaterializeMissingPropertyAliases()
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var alias in PropertyRenameAliases)
            {
                var target = GetRawProperty(alias.NewPath.Split('.'));
                var source = GetRawProperty(alias.OldPath.Split('.'));
                if (target == null && source != null)
                {
                    SetRawProperty(source.DeepClone(), alias.NewPath.Split('.'));
                    changed = true;
                }
                else if (source == null && target != null)
                {
                    SetRawProperty(target.DeepClone(), alias.OldPath.Split('.'));
                    changed = true;
                }
            }
        } while (changed);
    }

    private IEnumerable<string> WritePaths(string requestedPath)
    {
        var paths = new List<string> { requestedPath };
        for (var index = 0; index < paths.Count; index++)
        {
            var path = paths[index];
            foreach (var alias in PropertyRenameAliases)
            {
                if (Matches(path, alias.OldPath))
                    AddPath(ReplacePrefix(path, alias.OldPath, alias.NewPath));
                if (Matches(path, alias.NewPath))
                    AddPath(ReplacePrefix(path, alias.NewPath, alias.OldPath));
            }
        }

        return paths;

        void AddPath(string path)
        {
            if (!paths.Contains(path, StringComparer.Ordinal))
                paths.Add(path);
        }
    }

    private BsonValue? GetRawProperty(string[] parts)
    {
        var rootValue = Properties.GetValueOrDefault(parts[0]);
        return parts.Length == 1
            ? rootValue
            : BsonConversionTools.NavigateNestedBsonValue(rootValue, parts.Skip(1));
    }

    private void SetRawProperty(BsonValue? value, string[] relevantParts)
    {
        if (relevantParts.Length == 1)
        {
            if (value == null)
                Properties.Remove(relevantParts[0]);
            else
                Properties[relevantParts[0]] = value;
            return;
        }

        if (!Properties.TryGetValue(relevantParts[0], out var document) || document.IsBsonNull)
        {
            if (value == null)
                return;
            Properties[relevantParts[0]] = document = new BsonDocument();
        }

        foreach (var part in relevantParts[1..^1])
        {
            if (!document.AsBsonDocument.Contains(part))
            {
                if (value == null)
                    return;
                document.AsBsonDocument.Add(part, new BsonDocument());
            }

            document = document.AsBsonDocument[part];
        }

        // null means unset, same as the single-part branch above
        if (value == null)
            document.AsBsonDocument.Remove(relevantParts[^1]);
        else
            document[relevantParts[^1]] = value;
    }

    private static bool Matches(string path, string prefix)
        => path == prefix || path.StartsWith(prefix + '.', StringComparison.Ordinal);

    private static string ReplacePrefix(string path, string oldPrefix, string newPrefix)
        => newPrefix + path[oldPrefix.Length..];

    /// <summary>
    /// Transitions the workflow to a new step
    /// </summary>
    public void TransitionToStep(string newStep)
    {
        CurrentStep = newStep;
    }

    /// <summary>
    /// Records an event in the workflow
    /// </summary>
    public InstanceEvent RecordEvent(string eventId, DateTime? date = null)
    {
        var newEvent = new InstanceEvent
        {
            Id = eventId,
            Date = date ?? NextEventDate()
        };
        Events[eventId] = newEvent;
        return newEvent;
    }

    /// <summary>
    /// A strictly increasing UTC timestamp for a new event. Suppression orders events by a strict
    /// comparison on <see cref="InstanceEvent.Date"/>, so two events in one instance must never share
    /// a timestamp; MongoDB stores millisecond precision, hence the 1ms floor.
    /// </summary>
    private DateTime NextEventDate()
    {
        var now = DateTime.UtcNow;
        var candidate = Events.Values
            .Where(e => e.Date != null)
            .Select(e => e.Date!.Value)
            .DefaultIfEmpty()
            .Max()
            .AddMilliseconds(1);
        return now > candidate ? now : candidate;
    }

    /// <summary>
    /// Checks if an event has occurred
    /// </summary>
    public bool HasEvent(string eventId)
        => Events.ContainsKey(eventId);

    /// <summary>
    /// Gets the date when an event occurred
    /// </summary>
    public DateTime? GetEventDate(string eventId)
        => Events.TryGetValue(eventId, out var evt) ? evt.Date : null;

    /// <summary>
    /// Clears a property value
    /// </summary>
    public void ClearProperty(string property)
    {
        SetProperty(null, property);
    }

    /// <summary>
    /// Validates that required properties are set
    /// </summary>
    public bool ValidateRequiredProperties(params string[] requiredProperties)
    {
        return requiredProperties.All(HasAnswer);
    }
}

public record CurrencyAmount(string Currency, double Amount);