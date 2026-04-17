namespace UvA.Workflow.Users.EduId;

public record InviteUserResponse(
    string UserId,
    string Email,
    string UserName,
    string InvitationUrl);