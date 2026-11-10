using MongoDB.Bson.Serialization.Attributes;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Events;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventLogOperation
{
    Create,
    Update,

    /// <summary>Removes one event as part of normal workflow execution.</summary>
    Delete,

    /// <summary>Invalidates the operation identified by OperationId.</summary>
    Undo
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OperationType
{
    FormSubmission,
    ExecuteAction
}

[BsonIgnoreExtraElements]
public record OperationMetadata
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = ObjectId.GenerateNewId().ToString();

    [BsonRepresentation(BsonType.String)] public OperationType Type { get; init; }
    public string Source { get; init; } = null!;
    [BsonIgnoreIfNull] public string? Step { get; init; }

    public static OperationMetadata? CreateForSubmission(Form form, WorkflowDefinition workflowDefinition)
        => Create(OperationType.FormSubmission, form.Name,
            workflowDefinition.AllSteps
                .SelectMany(step => step.Actions)
                .Where(action => action.Type == RoleAction.Submit && action.MatchesForm(form.Name))
                .SelectMany(action => action.Steps)
                .Distinct(),
            workflowDefinition);

    public static OperationMetadata? CreateForAction(WorkflowModel.Action action,
        WorkflowDefinition workflowDefinition)
    {
        if (action.Name == null)
            return null;
        if (!workflowDefinition.GlobalActions.Contains(action))
            return Create(OperationType.ExecuteAction, action.Name, action.Steps, workflowDefinition);
        if (action.Steps.Length != 0)
            return null;

        var grants = workflowDefinition.GlobalActions.Concat(workflowDefinition.Roles.SelectMany(role => role.Actions));
        return grants.Any(grant => IsGlobalUndoGrant(grant, action.Name))
            ? new OperationMetadata { Type = OperationType.ExecuteAction, Source = action.Name }
            : null;
    }

    internal static bool IsGlobalUndoGrant(WorkflowModel.Action action, string source)
        => action.Type == RoleAction.Undo && action.Steps.Length == 0 && action.AllForms.Length == 0 &&
           action.Name == source;

    private static OperationMetadata? Create(OperationType type, string source, IEnumerable<string?> owningSteps,
        WorkflowDefinition workflowDefinition)
    {
        var stepNames = owningSteps.ToArray();
        if (stepNames is not [string stepName])
            return null;

        var step = workflowDefinition.AllSteps.SingleOrDefault(candidate => candidate.Name == stepName);
        if (step == null)
            return null;

        return new OperationMetadata
        {
            Type = type,
            Source = source,
            Step = step.Name
        };
    }
}

[BsonIgnoreExtraElements]
public class InstanceEventLogEntry
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("Timestamp")] public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [BsonElement("InstanceId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string WorkflowInstanceId { get; set; } = null!;

    [BsonIgnoreIfNull]
    [BsonElement("EventId")]
    public string EventId { get; set; } = null!;

    [BsonElement("ExecutedBy")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string ExecutedBy { get; set; } = null!;

    [BsonElement("EventDate")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? EventDate { get; set; }

    [BsonRepresentation(BsonType.String)]
    [BsonElement("Operation")]
    public EventLogOperation Operation { get; set; }

    /// <summary>
    /// The operation this event change belongs to, or the operation
    /// invalidated by an undo entry.
    /// </summary>
    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? OperationId { get; set; }

    /// <summary>
    /// Present only on the first event change of an undoable operation.
    /// </summary>
    [BsonIgnoreIfNull]
    public OperationMetadata? OperationMetadata { get; set; }

    /// <summary>Undo rows only: why the operation was undone.</summary>
    [BsonIgnoreIfNull]
    public string? Reason { get; set; }
}