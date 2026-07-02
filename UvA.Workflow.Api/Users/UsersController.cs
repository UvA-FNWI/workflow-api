using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Users.Dtos;
using UvA.Workflow.Users;
using UvA.Workflow.Users.DataNose;

namespace UvA.Workflow.Api.Users;

public class UsersController(
    IUserService userService,
    IUserRepository userRepository,
    RightsService rightsService,
    IEduIdUserService eduIdUserService,
    HttpContextCurrentUserAccessor realUserAccessor,
    UserImpersonationTokenService userImpersonationTokenService,
    ILogger<UsersController> logger) : ApiControllerBase
{
    private const string ValidEmailStatus = "Valid";
    private const string ManualUserInternalEmailCode = "ManualUserInternalEmail";
    private const string ManualUserEmailAlreadyExistsCode = "ManualUserEmailAlreadyExists";
    private const string InvalidEmailAddressCode = "InvalidEmailAddress";
    private const string ImpersonationTargetNotFoundCode = "ImpersonationTargetNotFound";

    private const string InternalEmailMessage = "Internal email address";
    private const string DuplicateEmailMessage = "Email already exists";
    private const string InvalidEmailMessage = "Invalid email address";
    private const string ImpersonationTargetNotFoundMessage = "User has no workflow account yet";

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

        var emailValidationResult = await ValidateEmail(dto.Email, ct);
        if (emailValidationResult != null)
            return emailValidationResult;

        var user = new User
        {
            UserName = dto.UserName,
            DisplayName = dto.DisplayName,
            Email = dto.Email.Trim(),
            PreferredLanguage = dto.PreferredLanguage,
            Organization = dto.Organization
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

    [HttpPut("{id}/email")]
    public async Task<ActionResult<UserDto>> UpdateEmail(
        string id,
        [FromBody] UpdateUserEmailDto dto,
        CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.Edit);

        var user = await userRepository.GetById(id, ct);
        if (user == null)
            return UserNotFound;

        if (!CanUpdateExternalUserEmail(user))
        {
            return Unprocessable(
                "UserEmailUpdateNotAllowed",
                "Email address can only be updated for external users that have not started an invitation");
        }

        var emailValidationResult = await ValidateEmail(dto.Email, ct, user.Id);
        if (emailValidationResult != null)
            return emailValidationResult;

        var previousEmail = user.Email;
        var email = dto.Email.Trim();
        if (string.Equals(user.Email, email, StringComparison.Ordinal))
            return Ok(UserDto.Create(user));

        user.Email = email;
        if (string.Equals(user.UserName, previousEmail, StringComparison.OrdinalIgnoreCase))
            user.UserName = email;

        await userRepository.Update(user, ct);
        return Ok(UserDto.Create(user));
    }

    [HttpGet("find")]
    public async Task<ActionResult<IEnumerable<UserSearchResultDto>>> Find(string query,
        [FromQuery] bool includeExternalUsers = true, CancellationToken ct = default)
    {
        var searchResults = await userService.FindUsers(query, includeExternalUsers, ct);
        return Ok(searchResults.Select(UserSearchResultDto.Create));
    }

    /// <summary>
    /// Starts impersonating another user. Returns a signed token the client sends back via
    /// the <c>X-User-Impersonation</c> header; from then on the API resolves the current user as the
    /// target. The real admin keeps their SurfConext identity, so authorisation here always checks the
    /// admin even when an impersonation is already active (enabling re-targeting, blocking escalation).
    /// </summary>
    [HttpPost("impersonate")]
    public async Task<ActionResult<UserImpersonationStartedDto>> Impersonate(
        [FromBody] StartUserImpersonationDto dto, CancellationToken ct)
    {
        var realName = realUserAccessor.GetCurrentUserName();
        if (string.IsNullOrWhiteSpace(realName))
            return UserNotFound;

        var realUser = await userService.GetUser(realName, ct);
        var roles = realUser is null ? [] : await userService.GetRoles(realUser, ct);
        if (!roles.Contains(DataNoseDirectoryKeys.SuperAdminRoleName, StringComparer.OrdinalIgnoreCase))
            return Forbidden();

        var target = await userService.GetUser(dto.UserName, ct);
        if (target == null)
            return NotFound(ImpersonationTargetNotFoundCode, ImpersonationTargetNotFoundMessage);

        var token = userImpersonationTokenService.CreateToken(realName, target.UserName);
        logger.LogInformation("{Admin} started impersonating {Target}", realName, target.UserName);

        return Ok(new UserImpersonationStartedDto(token.Value, token.ExpiresAtUtc));
    }

    private async Task<ObjectResult?> ValidateEmail(string email, CancellationToken ct)
    {
        var trimmedEmail = email.Trim();
        if (!EmailAddressAttribute.IsValid(trimmedEmail))
            return BadRequest(InvalidEmailAddressCode, InvalidEmailMessage);

        if (eduIdUserService.IsInternalEmailAddress(trimmedEmail))
            return BadRequest(ManualUserInternalEmailCode, InternalEmailMessage);

        var existingUser = await userRepository.GetByEmail(trimmedEmail, ct);
        if (existingUser != null &&
            (ignoredUserId == null || !string.Equals(existingUser.Id, ignoredUserId, StringComparison.Ordinal)))
            return Conflict(ManualUserEmailAlreadyExistsCode, DuplicateEmailMessage);

        return null;
    }

    private static bool CanUpdateExternalUserEmail(User user)
        => UserProviderKeys.IsExternal(user.ProviderKey) &&
           user.InvitationState == UserInvitationState.Required;
}