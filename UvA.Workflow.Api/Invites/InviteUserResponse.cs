namespace UvA.Workflow.Api.Invites;

public record InviteUserResponse(
    string UserId,
    string Email,
    string UserName,
    string InvitationUrl);