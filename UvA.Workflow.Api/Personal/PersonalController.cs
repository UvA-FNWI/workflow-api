using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Personal.Dtos;
using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.Users.Dtos;

namespace UvA.Workflow.Api.Personal;

public class PersonalController(
    IUserService userService,
    IUserRepository userRepository,
    PersonalInstanceService personalInstanceService,
    InstanceAuthorizationFilterService authorizationFilterService) : ApiControllerBase
{
    private const int SearchResultLimit = 50;

    [HttpGet("Me")]
    public async Task<ActionResult<PersonalInstancesDto>> GetMyInstances(CancellationToken ct)
    {
        var currentUser = await userService.GetCurrentUser(ct);
        if (currentUser == null)
            return Unauthorized();

        return Ok(await personalInstanceService.GetInstances(currentUser, ct));
    }

    [HttpGet("Search")]
    public async Task<ActionResult<PeopleSearchDto>> Search(string? query, CancellationToken ct)
    {
        if (!await authorizationFilterService.CanAccessAnyWorkflowOverview(ct))
            return Forbidden();

        var term = query?.Trim();
        if (term is not { Length: >= 2 and <= 200 })
            return Ok(new PeopleSearchDto([], false));

        var people = new List<UserDto>();
        await foreach (var users in userRepository.SearchByPrefix(term, ct).Chunk(SearchResultLimit)
                           .WithCancellation(ct))
        {
            people.AddRange(await GetVisiblePeople(users, ct));
            if (people.Count > SearchResultLimit)
                break;
        }

        return Ok(new PeopleSearchDto(people.Take(SearchResultLimit).ToArray(), people.Count > SearchResultLimit));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PersonalInstancesDto>> GetUserInstances(string id, CancellationToken ct)
    {
        if (!await authorizationFilterService.CanAccessAnyWorkflowOverview(ct))
            return Forbidden();

        var user = await userRepository.GetById(id, ct);
        if (user == null)
            return UserNotFound;

        var overview = await personalInstanceService.GetPersonInstances(user, ct);
        if (overview.Instances.Length == 0)
            return UserNotFound;

        return Ok(overview);
    }

    private async Task<IEnumerable<UserDto>> GetVisiblePeople(User[] users, CancellationToken ct)
    {
        var candidates = new List<(User User, ObjectId Id)>();
        foreach (var user in users)
            if (ObjectId.TryParse(user.Id, out var id))
                candidates.Add((user, id));

        var visibleIds = await personalInstanceService.GetVisibleUserIds(
            candidates.Select(candidate => candidate.Id).ToArray(), ct);
        return candidates
            .Where(candidate => visibleIds.Contains(candidate.Id))
            .Select(candidate => UserDto.Create(candidate.User));
    }
}