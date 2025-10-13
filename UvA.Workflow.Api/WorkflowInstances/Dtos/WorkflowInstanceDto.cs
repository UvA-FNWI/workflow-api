using UvA.Workflow.Api.Actions.Dtos;
using UvA.Workflow.Api.EntityTypes.Dtos;
using UvA.Workflow.Api.Submissions.Dtos;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public record WorkflowInstanceBasicDto(
    string Id,
    string? CurrentStep
);


public record WorkflowInstanceDto(
    string Id,
    EntityTypeDto EntityType,
    string? CurrentStep,
    Dictionary<string, object> Properties,
    Dictionary<string, InstanceEventDto> Events,
    string? ParentId,
    ActionDto[] Actions,
    FieldDto[] Fields,
    StepDto[] Steps,
    SubmissionDto[] Submissions,
    RoleAction[] Permissions
);

public record FieldDto();

public record StepDto();

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
                action.Action.Label ?? Add(action.EntityType?.DisplayTitle ?? "form"),
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
                Form: action.Action.Name ?? action.Form?.Name
            ),
            _ => throw new ArgumentOutOfRangeException()
        };
    
    private static BilingualString Add(BilingualString target) => new($"Add {target.En.ToLower()}", $"{target.Nl} toevoegen");
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