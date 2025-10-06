using Microsoft.AspNetCore.Mvc;
using UvA.Workflow.Api.Features.Users.Dtos;
using Uva.Workflow.Users;

namespace UvA.Workflow.Api.Features.Users;

[ApiController]
[Route("api/users")]
public class UsersController(UserService userService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<UserDto>> Create([FromBody] CreateUserDto dto)
    {
        try
        {
            var user = new User
            {
                ExternalId = dto.ExternalId,
                DisplayName = dto.DisplayName,
                Email = dto.Email
            };

            await userService.CreateAsync(user);
            var userDto = UserDto.From(user);

            return CreatedAtAction(nameof(GetById), new { id = user.Id }, userDto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetById(string id)
    {
        try
        {
            var user = await userService.GetByIdAsync(id);
            if (user == null)
            {
                return NotFound(new { error = $"User with ID '{id}' not found" });
            }

            return Ok(UserDto.From(user));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}