using System.ComponentModel.DataAnnotations;
using UvA.Workflow.WorkflowModel;
using Domain_Action = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public record ImpersonationRoleDto(
    string Name,
    BilingualString Title)
{
    public static ImpersonationRoleDto Create(WorkflowImpersonationRole role)
        => new(role.Name, role.Title);
}

public record StartImpersonationDto(
    [Required] string Role
);

public record StartImpersonationResultDto(
    string InstanceId,
    WorkflowImpersonationRole Role,
    string Token,
    DateTime ExpiresAtUtc
);

public record ActiveStepDto(
    string Name,
    BilingualString Title,
    bool IsCurrent,
    ActiveStepRoleDto[] Roles
);

public record ActiveStepRoleDto(
    string Name,
    BilingualString Title,
    AllowedActionDto[] Actions
);

// Keep Label nullable; the UI falls back to Type and Target.
public record AllowedActionDto(
    RoleAction Type,
    BilingualString? Label,
    string? Target
)
{
    public static AllowedActionDto Create(Domain_Action action)
        => new(action.Type, action.Label, GetTarget(action));

    // The UI combines this target with the type for unlabeled actions.
    private static string? GetTarget(Domain_Action action)
        => new[]
            {
                action.Name, string.Join(", ", action.AllForms), action.PropertyDefinition, action.Property
            }
            .FirstOrDefault(s => !string.IsNullOrEmpty(s));
}