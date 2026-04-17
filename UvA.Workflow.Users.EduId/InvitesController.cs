using UvA.Workflow.Api.Authentication.Abstractions;

namespace UvA.Workflow.Users.EduId;

/// <summary>
/// Creates a pending EduID user and EduID invitation via <see cref="IEduIdUserService"/>.
/// </summary>
[ApiController]
[Route("[controller]")]
[Microsoft.AspNetCore.Authorization.Authorize(AuthenticationSchemes = WorkflowAuthenticationDefaults.UserScheme)]
public class InvitesController(IEduIdUserService eduIdUserService) : ControllerBase
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
                    CreateProblem(StatusCodes.Status400BadRequest, ex),
                EduIdInviteFailureReason.PendingInvitation or EduIdInviteFailureReason.UserAlreadyExists =>
                    CreateProblem(StatusCodes.Status409Conflict, ex),
                EduIdInviteFailureReason.MissingInvitationUrl =>
                    CreateProblem(StatusCodes.Status422UnprocessableEntity, ex),
                _ => CreateProblem(StatusCodes.Status422UnprocessableEntity, ex)
            };
        }
    }

    private ObjectResult CreateProblem(int statusCode, EduIdInviteException ex)
        => new(new ProblemDetails
        {
            Status = statusCode,
            Title = ex.Reason.ToString(),
            Detail = ex.Message
        })
        {
            StatusCode = statusCode
        };
}
