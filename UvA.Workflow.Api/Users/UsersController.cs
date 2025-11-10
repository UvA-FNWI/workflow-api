using System.Security.Claims;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Users.Dtos;

namespace UvA.Workflow.Api.Users;

public class UsersController(IUserService userService, IUserRepository userRepository) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<UserDto>> Create([FromBody] CreateUserDto dto, CancellationToken ct)
    {
        var user = new User
        {
            ExternalId = dto.ExternalId,
            DisplayName = dto.DisplayName,
            Email = dto.Email
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
    public async Task<ActionResult<IEnumerable<ExternalUser>>> Find(string query, CancellationToken ct)
    {
        return Ok(await userService.FindUsers(query, ct));
    }
    
    [HttpGet("roles")]
    public async Task<ActionResult<IEnumerable<string>>> Roles(string uid, CancellationToken ct)
    {
        var claims = new[]
        {
            new Claim("urn:uvanetid", uid)
        };

        var x = new ClaimsPrincipal(new ClaimsIdentity(claims));
        return Ok(await userService.GetRoles(x,ct));
    }
}