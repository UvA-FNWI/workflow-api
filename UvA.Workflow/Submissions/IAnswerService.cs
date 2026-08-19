using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Submissions;

public interface IAnswerService
{
    Task SavePropertyValue(
        WorkflowInstance instance,
        string[] pathParts,
        PropertyDefinition propertyDefinition,
        BsonValue newValue,
        bool shouldLog,
        CancellationToken ct);
}