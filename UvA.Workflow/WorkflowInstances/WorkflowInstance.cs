using MongoDB.Bson.Serialization.Attributes;
using UvA.Workflow.Events;

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

    public string EntityType { get; set; } = null!;
    public string? CurrentStep { get; set; }

    // public List<LogEntry> LogEntries { get; set; } = [];
    public Dictionary<string, BsonValue> Properties { get; set; } = null!;
    public Dictionary<string, InstanceEvent> Events { get; set; } = null!;


    public string? ParentId { get; set; }

    public bool HasAnswer(string property)
        => Properties.TryGetValue(property, out var value) && value != BsonNull.Value;

    public BsonValue? GetProperty(params string?[] parts)
    {
        string[] relevantParts = parts.Where(p => p != null).ToArray()!;
        if (relevantParts.Length == 0) return null;

        var rootValue = Properties.GetValueOrDefault(relevantParts[0]);
        if (relevantParts.Length == 1)
            return rootValue;

        return BsonConversionTools.NavigateNestedBsonValue(rootValue, relevantParts.Skip(1));
    }

    public void SetProperty(BsonValue value, params string?[] parts)
    {
        string[] relevantParts = parts.Where(p => p != null).ToArray()!;
        if (relevantParts.Length == 1)
        {
            Properties[relevantParts[0]] = value;
            return;
        }

        if (!Properties.TryGetValue(relevantParts[0], out var document) || document.IsBsonNull)
            Properties[relevantParts[0]] = document = new BsonDocument();
        foreach (var part in parts.Skip(1).Take(parts.Length - 2))
        {
            if (!document.AsBsonDocument.Contains(part))
                document.AsBsonDocument.Add(part, new BsonDocument());
            document = document.AsBsonDocument[part];
        }

        document[parts.Last()] = value;
    }

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
            Date = date ?? DateTime.UtcNow
        };
        Events[eventId] = newEvent;
        return newEvent;
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
        Properties.Remove(property);
    }

    /// <summary>
    /// Validates that required properties are set
    /// </summary>
    public bool ValidateRequiredProperties(params string[] requiredProperties)
    {
        return requiredProperties.All(HasAnswer);
    }
}

public record StateLogEntry(string State, DateTime Date, string? UserId);

public record CurrencyAmount(string Currency, double Amount);

public enum MessageKind
{
    Normal,
    Close
}