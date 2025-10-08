using UvA.Workflow.Api.Exceptions;
using UvA.Workflow.Api.Extensions;

namespace UvA.Workflow.Api.Features.Users;

public class UsersController(IUserRepository userRepository) : ApiControllerBase
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
        var userDto = UserDto.From(user);

        return CreatedAtAction(nameof(GetById), new { id = user.Id }, userDto);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetById(string id, CancellationToken ct)
    {
        var user = await userRepository.GetById(id, ct);
        if (user == null)
            return ErrorCode.UsersNotFound;

        return Ok(UserDto.From(user));
    }
}