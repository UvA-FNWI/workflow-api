using MongoDB.Bson.Serialization.Attributes;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Events;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventLogOperation
{
    Create,
    Update,
    Delete
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OperationType
{
    FormSubmission,
    ExecuteAction
}

public record OperationMetadata
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = ObjectId.GenerateNewId().ToString();

    [BsonRepresentation(BsonType.String)] public OperationType Type { get; init; }
    public string Source { get; init; } = null!;
    public string Step { get; init; } = null!;
    public string TopLevelStep { get; init; } = null!;
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    [BsonRepresentation(BsonType.ObjectId)]
    public string ExecutedBy { get; init; } = null!;

    public long? Revision { get; init; }

    public static OperationMetadata? CreateForSubmission(Form form, WorkflowDefinition workflowDefinition, User user)
        => Create(OperationType.FormSubmission, form.Name,
            workflowDefinition.AllSteps
                .SelectMany(step => step.Actions)
                .Where(action => action.Type == RoleAction.Submit && action.MatchesForm(form.Name))
                .SelectMany(action => action.Steps)
                .Distinct(),
            workflowDefinition, user);

    public static OperationMetadata? CreateForAction(WorkflowModel.Action action,
        WorkflowDefinition workflowDefinition, User user)
        => action.Name == null || workflowDefinition.GlobalActions.Contains(action)
            ? null
            : Create(OperationType.ExecuteAction, action.Name, action.Steps, workflowDefinition, user);

    private static OperationMetadata? Create(OperationType type, string source, IEnumerable<string?> owningSteps,
        WorkflowDefinition workflowDefinition, User user)
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
            TopLevelStep = topLevelStep.Name,
            ExecutedBy = user.Id
        };
    }
}

public class InstanceEventLogEntry
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("Timestamp")] public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [BsonElement("InstanceId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string WorkflowInstanceId { get; set; } = null!;

    [BsonElement("EventId")] public string EventId { get; set; } = null!;

    [BsonElement("ExecutedBy")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string ExecutedBy { get; set; } = null!;

    [BsonElement("EventDate")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? EventDate { get; set; }

    [BsonRepresentation(BsonType.String)]
    [BsonElement("Operation")]
    public EventLogOperation Operation { get; set; }

    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? OperationId { get; set; }

    [BsonIgnoreIfNull] public OperationMetadata? OperationMetadata { get; set; }
}