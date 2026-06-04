using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Users.Dtos;
using UvA.Workflow.Users;
using UvA.Workflow.Users.DataNose;

namespace UvA.Workflow.Api.Users;

public class UsersController(
    IUserService userService,
    IUserRepository userRepository,
    RightsService rightsService) : ApiControllerBase
{
    /// <summary>
    /// Returns the currently authenticated user.
    /// </summary>
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> GetLoggedInUser(CancellationToken ct)
    {
        var user = await userService.GetCurrentUser(ct);
        if (user == null)
            return UserNotFound;

        // Developer tooling is restricted to DataNose super admins (the SystemAdmin role). The role
        // name arrives from DataNose via GetRolesForUser; see DataNoseDirectoryKeys.
        var roles = await userService.GetRolesOfCurrentUser(ct);
        var isSuperAdmin = roles.Contains(DataNoseDirectoryKeys.SuperAdminRoleName, StringComparer.OrdinalIgnoreCase);
        return Ok(UserDto.Create(user, isSuperAdmin));
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create([FromBody] CreateUserDto dto, CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);

        var user = new User
        {
            UserName = dto.UserName,
            DisplayName = dto.DisplayName,
            Email = dto.Email,
            PreferredLanguage = dto.PreferredLanguage
        };

        await userRepository.Create(user, ct);
        var userDto = UserDto.Create(user);

        return CreatedAtAction(nameof(GetById), new { id = user.Id }, userDto);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetById(string id, CancellationToken ct)
    {
        var user = await userRepository.GetById(id, ct);
        if (user == null)
            return UserNotFound;

        return Ok(UserDto.Create(user));
    }

    [HttpGet("find")]
    public async Task<ActionResult<IEnumerable<UserSearchResultDto>>> Find(string query, CancellationToken ct)
    {
        var searchResults = await userService.FindUsers(query, ct);
        return Ok(searchResults.Select(UserSearchResultDto.Create));
    }
}