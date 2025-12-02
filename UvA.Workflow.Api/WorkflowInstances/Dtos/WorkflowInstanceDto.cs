using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.Events;

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

public record FieldDto();

public record StepDto(
    string Id,
    BilingualString Title,
    string? Event,
    DateTime? DateCompleted,
    DateTime? Deadline,
    StepDto[]? Children)
{
    public static StepDto Create(Step step, WorkflowInstance instance, ModelService modelService)
        => new(
            step.Name,
            step.DisplayTitle,
            step.EndEvent,
            step.GetEndDate(instance),
            step.GetDeadline(instance, modelService),
            step.Children.Length != 0 ? step.Children.Select(s => Create(s, instance, modelService)).ToArray() : null
        );
}

public record ActionDto(
    ActionType Type,
    BilingualString Title,
    string? Form = null,
    string? Name = null,
    string? UserId = null,
    Mail? Mail = null,
    string? Property = null
)
{
    public string Id => $"{Type}_{Name ?? Property ?? Form ?? UserId}";

    public static ActionDto Create(InstanceService.AllowedAction action) =>
        action.Action.Type switch
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
                Form: action.Form?.Name
            ),
            _ => throw new ArgumentOutOfRangeException()
        };

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