using MongoDB.Bson.Serialization.Attributes;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Events;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventLogOperation
{
    Create,
    Update,
    Delete,

    /// <summary>Invalidates one undoable operation. The target is TargetOperationId, not OperationId.</summary>
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
    public string Step { get; init; } = null!;
    public string TopLevelStep { get; init; } = null!;

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
        => action.Name == null || workflowDefinition.GlobalActions.Contains(action)
            ? null
            : Create(OperationType.ExecuteAction, action.Name, action.Steps, workflowDefinition);

    private static OperationMetadata? Create(OperationType type, string source, IEnumerable<string?> owningSteps,
        WorkflowDefinition workflowDefinition)
    {
        var stepNames = owningSteps.ToArray();
        if (stepNames is not [string stepName])
            return null;

        var step = workflowDefinition.AllSteps.SingleOrDefault(candidate => candidate.Name == stepName);
        if (step == null)
            return null;

        var topLevelStep = step;
        while (topLevelStep.ParentStep != null)
            topLevelStep = topLevelStep.ParentStep;

        return new OperationMetadata
        {
            Type = type,
            Source = source,
            Step = step.Name,
            TopLevelStep = topLevelStep.Name
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
    /// The undoable operation this event mutation belongs to. Set on the root and on
    /// every later consequence (including delayed ones). Projection drops these rows
    /// when an undo targets that same id.
    /// </summary>
    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? OperationId { get; set; }

    /// <summary>
    /// Present only on the first event of an undoable operation. Identifies the
    /// submission or action and which step owns it.
    /// </summary>
    [BsonIgnoreIfNull]
    public OperationMetadata? OperationMetadata { get; set; }

    /// <summary>
    /// Undo rows only: which operation to drop, for example <c>op-1</c> after a later
    /// submit <c>op-2</c> should stay. This is not <see cref="OperationId"/> — an undo
    /// is not a consequence of the operation it invalidates.
    /// </summary>
    [BsonIgnoreIfNull]
    public string? TargetOperationId { get; set; }

    /// <summary>Undo rows only: why the operation was undone.</summary>
    [BsonIgnoreIfNull]
    public string? Reason { get; set; }
}