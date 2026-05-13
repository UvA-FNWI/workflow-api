using System.ComponentModel.DataAnnotations;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Users.Dtos;
using UvA.Workflow.Users;

namespace UvA.Workflow.Api.Users;

public class UsersController(
    IUserService userService,
    IUserRepository userRepository,
    RightsService rightsService,
    IEduIdUserService eduIdUserService) : ApiControllerBase
{
    private const string ValidEmailStatus = "Valid";
    private const string ManualUserInternalEmailCode = "ManualUserInternalEmail";
    private const string ManualUserEmailAlreadyExistsCode = "ManualUserEmailAlreadyExists";
    private const string InvalidEmailAddressCode = "InvalidEmailAddress";

    private const string InternalEmailMessage = "Internal email address";
    private const string DuplicateEmailMessage = "Email already exists";
    private const string InvalidEmailMessage = "Invalid email address";

    private static readonly EmailAddressAttribute EmailAddressAttribute = new();

    /// <summary>
    /// Returns the currently authenticated user.
    /// </summary>
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> GetLoggedInUser(CancellationToken ct)
    {
        var user = await userService.GetCurrentUser(ct);
        if (user == null)
            return UserNotFound;

        return Ok(UserDto.Create(user));
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create([FromBody] CreateUserDto dto, CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);

        var emailValidationResult = await ValidateEmail(dto.Email, ct);
        if (emailValidationResult != null)
            return emailValidationResult;

        var user = new User
        {
            UserName = dto.UserName,
            DisplayName = dto.DisplayName,
            Email = dto.Email.Trim(),
            PreferredLanguage = dto.PreferredLanguage
        };

        await userRepository.Create(user, ct);
        var userDto = UserDto.Create(user);

        return CreatedAtAction(nameof(GetById), new { id = user.Id }, userDto);
    }

    [HttpPost("verify-email")]
    public async Task<ActionResult<VerifyEmailResponse>> VerifyEmail(
        [FromBody] VerifyEmailRequest? request,
        CancellationToken ct)
    {
        var email = request?.Email.Trim() ?? string.Empty;
        var emailValidationResult = await ValidateEmail(email, ct);
        if (emailValidationResult != null)
            return emailValidationResult;

        return Ok(new VerifyEmailResponse(email, ValidEmailStatus));
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

    private async Task<ObjectResult?> ValidateEmail(string email, CancellationToken ct)
    {
        var trimmedEmail = email.Trim();
        if (!EmailAddressAttribute.IsValid(trimmedEmail))
            return BadRequest(InvalidEmailAddressCode, InvalidEmailMessage);

        if (eduIdUserService.IsInternalEmailAddress(trimmedEmail))
            return BadRequest(ManualUserInternalEmailCode, InternalEmailMessage);

        var existingUser = await userRepository.GetByEmail(trimmedEmail, ct);
        if (existingUser != null)
            return Conflict(ManualUserEmailAlreadyExistsCode, DuplicateEmailMessage);

        return null;
    }
}