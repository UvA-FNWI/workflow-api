using System.ComponentModel.DataAnnotations;

namespace UvA.Workflow.Api.Screens.Dtos;

public record BulkActionInputDto([Required, MinLength(1)] string[] InstanceIds);

public enum BulkActionStatus
{
    Eligible,
    Unavailable,
    Succeeded,
    Failed
}

public record BulkActionRowDto(string InstanceId, BulkActionStatus Status);

public record BulkActionResultDto(BulkActionRowDto[] Rows);