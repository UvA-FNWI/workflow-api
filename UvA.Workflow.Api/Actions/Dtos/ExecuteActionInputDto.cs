namespace UvA.Workflow.Api.Actions.Dtos;

public record ExecuteActionInputDto(
    ActionType Type,
    string InstanceId,
    string? Name = null,
    MailMessage? Mail = null
);