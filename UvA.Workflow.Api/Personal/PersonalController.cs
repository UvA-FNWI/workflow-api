using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Personal.Dtos;

namespace UvA.Workflow.Api.Personal;

public class PersonalController(
    IUserService userService,
    PersonalInstanceService personalInstanceService) : ApiControllerBase
{
    [HttpGet("Instances")]
    public async Task<ActionResult<PersonalInstancesDto>> GetInstances(CancellationToken ct)
    {
        var currentUser = await userService.GetCurrentUser(ct);
        if (currentUser == null)
            return Unauthorized();

        return Ok(await personalInstanceService.GetInstances(currentUser, ct));
    }
}