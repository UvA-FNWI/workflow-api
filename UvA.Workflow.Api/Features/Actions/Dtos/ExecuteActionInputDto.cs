namespace UvA.Workflow.Api.Features.Actions.Dtos;

public record ExecuteActionInputDto(
    ActionType Type,
    string InstanceId,
    string? Name = null,
    MailMessage? Mail = null
);
