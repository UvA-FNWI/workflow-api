using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.Events;
using UvA.Workflow.Versioning;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public record WorkflowInstanceBasicDto(
    string Id,
    string? CurrentStep
);

public record WorkflowInstanceDto(
    string Id,
    string? Title,
    WorkflowDefinitionDto WorkflowDefinition,
    string? CurrentStep,
    string? ParentId,
    ActionDto[] Actions,
    FieldDto[] Fields,
    StepDto[] Steps,
    SubmissionDto[] Submissions,
    RoleAction[] Permissions
);

public record FieldDto(BilingualString Title, object? Value);

public record StepDto(
    string Id,
    BilingualString Title,
    string? Event,
    DateTime? DateCompleted,
    DateTime? Deadline,
    StepDto[]? Children,
    List<StepVersion>? Versions = null)
{
    public static StepDto Create(Step step, WorkflowInstance instance, ModelService modelService,
        List<StepVersion>? versions = null)
    {
        var workflowDef = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        return new(
            step.Name,
            step.DisplayTitle,
            step.EndEvent,
            step.GetEndDate(instance, workflowDef),
            step.GetDeadline(instance, modelService),
            step.Children.Length != 0 ? step.Children.Select(s => Create(s, instance, modelService)).ToArray() : null,
            versions
        );
    }
}

public record ActionDto(
    ActionType Type,
    BilingualString Title,
    string? Form = null,
    string? Name = null,
    string? UserId = null,
    Mail? Mail = null,
    string? Property = null,
    string? Step = null,
    ActionIntent Intent = ActionIntent.Primary,
    FormLayout? FormLayout = null
)
{
    public string Id => $"{Type}_{Name ?? Property ?? Form ?? UserId}";

    public static ActionDto Create(InstanceService.AllowedAction action)
    {
        ActionDto dto = action.Action.Type switch
        {
            RoleAction.CreateRelatedInstance => new(
                ActionType.CreateInstance,
                action.Action.Label ?? Add(action.WorkflowDefinition?.DisplayTitle ?? "form"),
                Form: action.Action.Property
            ),
            RoleAction.Execute => new(
                ActionType.Execute,
                action.Action.Label ?? action.Action.Name ?? "Action",
                Name: action.Action.Name,
                Mail: action.Mail
            ),
            RoleAction.Submit => new(
                ActionType.SubmitForm,
                action.Action.Label ?? Add(action.Form?.Name ?? "form"),
                Form: action.Form?.Name,
                FormLayout: action.Form?.Layout
            ),
            _ => throw new ArgumentOutOfRangeException()
        };
        return dto with
        {
            Step = action.Action.Steps.Length == 1 ? action.Action.Steps[0] : null,
            Intent = action.Action.Intent
        };
    }

    private static BilingualString Add(BilingualString target) =>
        new($"Add {target.En.ToLower()}", $"{target.Nl} toevoegen");
}

public record InstanceEventDto(
    string Id,
    DateTime? Date
)
{
    /// <summary>
    /// Creates an InstanceEventDto from an InstanceEvent domain entity
    /// </summary>
    public static InstanceEventDto Create(InstanceEvent instanceEvent)
    {
        return new InstanceEventDto(instanceEvent.Id, instanceEvent.Date);
    }
}