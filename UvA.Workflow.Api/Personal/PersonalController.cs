using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Personal.Dtos;
using UvA.Workflow.Api.Users;
using UvA.Workflow.Api.Users.Dtos;

namespace UvA.Workflow.Api.Personal;

public class PersonalController(
    IUserService userService,
    PersonalInstanceService personalInstanceService,
    IUserRepository userRepository,
    PeopleSearchAccess searchAccess) : ApiControllerBase
{
    [HttpGet("Instances")]
    public async Task<ActionResult<PersonalInstancesDto>> GetInstances(CancellationToken ct)
    {
        var currentUser = await userService.GetCurrentUser(ct);
        if (currentUser == null)
            return Unauthorized();

        return Ok(await personalInstanceService.GetInstances(currentUser, ct));
    }

    [HttpGet("People/{personId}")]
    public async Task<ActionResult<PersonalPersonDto>> GetPerson(string personId, CancellationToken ct)
    {
        if (!await searchAccess.CanSearch(ct))
            return Forbidden();

        var person = await userRepository.GetById(personId, ct);
        if (person == null)
            return UserNotFound;

        var overview = await personalInstanceService.GetPersonInstances(person, ct);
        if (overview.Instances.Length == 0)
            return UserNotFound;

        return Ok(new PersonalPersonDto(PersonDto.Create(person), overview.Roles, overview.Instances));
    }
}