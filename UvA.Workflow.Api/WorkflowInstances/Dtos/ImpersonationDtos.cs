using System.ComponentModel.DataAnnotations;

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