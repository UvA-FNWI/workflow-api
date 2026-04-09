using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Api.Infrastructure;

namespace UvA.Workflow.Api.Invites;

/// <summary>
/// Creates a pending EduID user and EduID invitation via <see cref="IEduIdUserService"/>.
/// </summary>
public class InvitesController(IEduIdUserService eduIdUserService) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<InviteUserResponse>> SendInvite([FromBody] InviteUserRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await eduIdUserService.InviteUser(request.Email, request.UserName, ct);
            return Ok(new InviteUserResponse(
                result.User.Id,
                result.User.Email,
                result.User.DisplayName,
                result.InvitationUrl));
        }
        catch (EduIdInviteException ex)
        {
            return ex.Reason switch
            {
                EduIdInviteFailureReason.InternalEmail =>
                    BadRequest(ex.Reason.ToString(), ex.Message),
                EduIdInviteFailureReason.PendingInvitation or EduIdInviteFailureReason.UserAlreadyExists =>
                    Conflict(ex.Reason.ToString(), ex.Message),
                EduIdInviteFailureReason.MissingInvitationUrl =>
                    Unprocessable(ex.Reason.ToString(), ex.Message),
                _ => Unprocessable(ex.Reason.ToString(), ex.Message)
            };
        }
    }
}