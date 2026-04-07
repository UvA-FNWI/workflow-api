using System.Net;

namespace UvA.Workflow.Api.Authentication;

public enum EduIdInviteFailureReason
{
    InternalEmail,
    PendingInvitation,
    UserAlreadyExists,
    MissingInvitationUrl
}

public class EduIdInviteException(EduIdInviteFailureReason reason, string message) : InvalidOperationException(message)
{
    public EduIdInviteFailureReason Reason { get; } = reason;
}

public record EduIdUserInviteResult(User User, string InvitationUrl);

public interface IEduIdUserService
{
    bool IsInternalEmailAddress(string email);

    Task<EduIdUserInviteResult> InviteUser(string email, string displayName, CancellationToken ct = default);

    Task<User?> ResolveAuthenticatedUser(string uid, string displayName, string? email, CancellationToken ct = default);
}

public class EduIdUserService(
    IUserRepository userRepository,
    IEduIdInvitationClient invitationClient,
    IOptions<EduIdOptions> options,
    ILogger<EduIdUserService> logger) : IEduIdUserService
{
    private readonly EduIdOptions _options = options.Value;

    public bool IsInternalEmailAddress(string email)
    {
        var trimmedEmail = email.Trim();
        var atIndex = trimmedEmail.LastIndexOf('@');
        if (atIndex < 0 || atIndex == trimmedEmail.Length - 1)
            return false;

        var domain = trimmedEmail[(atIndex + 1)..];
        return _options.InternalEmailDomains.Any(internalDomain =>
            domain.Equals(internalDomain, StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith($".{internalDomain}", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<EduIdUserInviteResult> InviteUser(string email, string displayName,
        CancellationToken ct = default)
    {
        var trimmedEmail = email.Trim();
        var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName) ? trimmedEmail : displayName.Trim();

        if (IsInternalEmailAddress(trimmedEmail))
            throw new EduIdInviteException(EduIdInviteFailureReason.InternalEmail,
                $"The email address '{trimmedEmail}' belongs to an internal domain.");

        var existingUser = await userRepository.GetByEmail(trimmedEmail, ct);
        if (existingUser != null)
        {
            throw existingUser.IsActive
                ? new EduIdInviteException(EduIdInviteFailureReason.UserAlreadyExists,
                    $"A user with email '{trimmedEmail}' already exists.")
                : new EduIdInviteException(EduIdInviteFailureReason.PendingInvitation,
                    $"A pending EduID invitation already exists for '{trimmedEmail}'.");
        }

        var user = new User
        {
            UserName = trimmedEmail,
            DisplayName = resolvedDisplayName,
            Email = trimmedEmail,
            AuthProvider = UserAuthProvider.EduId,
            IsActive = false
        };

        var request = new EduIdInvitationRequest
        {
            IntendedAuthority = "GUEST",
            Message = string.Empty,
            Language = "en",
            EnforceEmailEquality = true,
            EduIdOnly = true,
            GuestRoleIncluded = true,
            SuppressSendingEmails = true,
            Invites = [trimmedEmail],
            RoleIdentifiers = [_options.RoleIdentifier],
            RoleExpiryDate = DateTime.Now.AddDays(_options.RoleExpiryDays),
            ExpiryDate = DateTime.Now.AddDays(_options.InvitationExpiryDays)
        };

        var response = await invitationClient.CreateInvitationAsync(request, ct);
        if (response.Status != (int)HttpStatusCode.OK && response.Status != (int)HttpStatusCode.Created)
            throw new InvalidOperationException($"Unexpected EduID invitation response status: {response.Status}.");

        var invitationUrl = response.RecipientInvitationUrls?
                                .FirstOrDefault(r =>
                                    string.Equals(r.Recipient, trimmedEmail, StringComparison.OrdinalIgnoreCase))
                                ?.InvitationUrl
                            ?? response.RecipientInvitationUrls?.FirstOrDefault()?.InvitationUrl;

        if (string.IsNullOrWhiteSpace(invitationUrl))
            throw new EduIdInviteException(EduIdInviteFailureReason.MissingInvitationUrl,
                $"The EduID invitation response did not contain an invitation URL for '{trimmedEmail}'.");

        await userRepository.Create(user, ct);
        logger.LogInformation("Created pending EduID user for {Email}", trimmedEmail);

        return new EduIdUserInviteResult(user, invitationUrl);
    }

    public async Task<User?> ResolveAuthenticatedUser(string uid,
        string displayName,
        string? email,
        CancellationToken ct = default)
    {
        var trimmedUid = uid.Trim();
        var trimmedEmail = email?.Trim();
        var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName) ? trimmedUid : displayName.Trim();

        var existingByUid = await userRepository.GetByExternalId(trimmedUid, ct);
        if (existingByUid?.AuthProvider == UserAuthProvider.EduId)
            return await ActivateUser(existingByUid, trimmedUid, resolvedDisplayName, trimmedEmail, ct);

        if (string.IsNullOrWhiteSpace(trimmedEmail))
            return null;

        var existingByEmail = await userRepository.GetByEmailAndProvider(trimmedEmail, UserAuthProvider.EduId, ct);
        return existingByEmail == null
            ? null
            : await ActivateUser(existingByEmail, trimmedUid, resolvedDisplayName, trimmedEmail, ct);
    }

    private async Task<User> ActivateUser(User user,
        string uid,
        string displayName,
        string? email,
        CancellationToken ct)
    {
        var changed = false;

        if (!string.Equals(user.UserName, uid, StringComparison.Ordinal))
        {
            user.UserName = uid;
            changed = true;
        }

        if (!string.Equals(user.DisplayName, displayName, StringComparison.Ordinal))
        {
            user.DisplayName = displayName;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(email) && !string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            user.Email = email;
            changed = true;
        }

        if (user.AuthProvider != UserAuthProvider.EduId)
        {
            user.AuthProvider = UserAuthProvider.EduId;
            changed = true;
        }

        if (!user.IsActive)
        {
            user.IsActive = true;
            changed = true;
        }

        if (changed)
        {
            await userRepository.Update(user, ct);
            logger.LogInformation("Activated EduID user {UserId}", user.Id);
        }

        return user;
    }
}