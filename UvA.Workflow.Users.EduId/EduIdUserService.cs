using System.ComponentModel.DataAnnotations;
using System.Net;

namespace UvA.Workflow.Users.EduId;

public class EduIdUserService(
    IUserRepository userRepository,
    IEduIdInvitationClient invitationClient,
    IOptions<EduIdOptions> options,
    ILogger<EduIdUserService> logger) : IEduIdUserService, IExternalUserService
{
    private static readonly EmailAddressAttribute EmailAddressAttribute = new();
    private readonly EduIdOptions _options = options.Value;

    public bool IsInternalEmailAddress(string email)
    {
        var trimmedEmail = email.Trim();
        var atIndex = trimmedEmail.LastIndexOf('@');
        if (atIndex < 0 || atIndex == trimmedEmail.Length - 1)
            throw new ArgumentException($"Invalid email address: {email}", nameof(email));

        var domain = trimmedEmail[(atIndex + 1)..];
        return _options.InternalEmailDomains.Any(internalDomain =>
            domain.Equals(internalDomain, StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith($".{internalDomain}", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<UserSearchResult> CreateOrUpdateExternalUser(
        string displayName,
        string email,
        Organization? organization,
        CancellationToken ct = default)
    {
        var trimmedEmail = email.Trim();
        if (string.IsNullOrWhiteSpace(trimmedEmail) || !EmailAddressAttribute.IsValid(trimmedEmail))
        {
            throw new ExternalUserCreationException(
                ExternalUserCreationFailureReason.InvalidEmailAddress,
                "Invalid email address");
        }

        if (IsInternalEmailAddress(trimmedEmail))
        {
            throw new ExternalUserCreationException(
                ExternalUserCreationFailureReason.InternalEmailAddress,
                "Internal email address");
        }

        var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? trimmedEmail
            : displayName.Trim();
        var existingUser = await userRepository.GetByEmail(trimmedEmail, ct);

        if (existingUser == null)
        {
            var user = new User
            {
                UserName = trimmedEmail,
                DisplayName = resolvedDisplayName,
                Email = trimmedEmail,
                Organization = organization,
                ProviderKey = EduIdDirectoryKeys.ProviderKey,
                IsActive = false
            };

            await userRepository.Create(user, ct);
            logger.LogInformation("Created external EduID user placeholder for {Email}", trimmedEmail);

            return CreateSearchResult(user);
        }

        if (!CanUpdateExternalPlaceholder(existingUser))
        {
            throw new ExternalUserCreationException(
                ExternalUserCreationFailureReason.UserAlreadyExists,
                "Email already exists");
        }

        var changed = false;
        if (!string.Equals(existingUser.DisplayName, resolvedDisplayName, StringComparison.Ordinal))
        {
            existingUser.DisplayName = resolvedDisplayName;
            changed = true;
        }

        if (!string.Equals(existingUser.Email, trimmedEmail, StringComparison.OrdinalIgnoreCase))
        {
            existingUser.Email = trimmedEmail;
            changed = true;
        }

        if (existingUser.Organization != organization)
        {
            existingUser.Organization = organization;
            changed = true;
        }

        if (changed)
            await userRepository.Update(existingUser, ct);

        return CreateSearchResult(existingUser);
    }

    public async Task<EduIdUserInviteResult> InviteUser(string email, string displayName,
        CancellationToken ct = default)
    {
        var result = await EnsureExternalAccount(email, displayName, EduIdInviteDeliveryMode.ReturnInvitationUrl, ct);
        return result.Status switch
        {
            EduIdExternalAccountStatus.InternalEmail => throw new EduIdInviteException(
                EduIdInviteFailureReason.InternalEmail,
                $"The email address '{email.Trim()}' belongs to an internal domain."),
            EduIdExternalAccountStatus.AlreadyActive => throw new EduIdInviteException(
                EduIdInviteFailureReason.UserAlreadyExists,
                $"A user with email '{email.Trim()}' already exists."),
            EduIdExternalAccountStatus.PendingInvitation => throw new EduIdInviteException(
                EduIdInviteFailureReason.PendingInvitation,
                $"A pending EduID invitation already exists for '{email.Trim()}'."),
            EduIdExternalAccountStatus.Invited when result.User != null &&
                                                    !string.IsNullOrWhiteSpace(result.InvitationUrl) =>
                new EduIdUserInviteResult(result.User, result.InvitationUrl),
            EduIdExternalAccountStatus.Invited => throw new EduIdInviteException(
                EduIdInviteFailureReason.MissingInvitationUrl,
                $"The EduID invitation response did not contain an invitation URL for '{email.Trim()}'."),
            _ => throw new InvalidOperationException($"Unsupported EduID invite status: {result.Status}.")
        };
    }

    public async Task<EduIdExternalAccountResult> EnsureExternalAccount(
        string email,
        string displayName,
        EduIdInviteDeliveryMode deliveryMode,
        CancellationToken ct = default)
    {
        var trimmedEmail = email.Trim();
        var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName) ? trimmedEmail : displayName.Trim();

        if (IsInternalEmailAddress(trimmedEmail))
            return new EduIdExternalAccountResult(EduIdExternalAccountStatus.InternalEmail);

        var existingUser = await userRepository.GetByEmail(trimmedEmail, ct);
        if (existingUser != null)
        {
            return existingUser.IsActive
                ? new EduIdExternalAccountResult(EduIdExternalAccountStatus.AlreadyActive, existingUser)
                : new EduIdExternalAccountResult(EduIdExternalAccountStatus.PendingInvitation, existingUser);
        }

        return await CreateExternalAccount(trimmedEmail, resolvedDisplayName, deliveryMode, ct);
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
        if (existingByUid != null && EduIdDirectoryKeys.IsEduId(existingByUid.ProviderKey))
            return await ActivateUser(existingByUid, trimmedUid, resolvedDisplayName, trimmedEmail, ct);

        if (string.IsNullOrWhiteSpace(trimmedEmail))
            return null;

        var existingByEmail =
            await userRepository.GetByEmailAndProvider(trimmedEmail, EduIdDirectoryKeys.ProviderKey, ct);
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

        if (!EduIdDirectoryKeys.IsEduId(user.ProviderKey))
        {
            user.ProviderKey = EduIdDirectoryKeys.ProviderKey;
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

    private static bool CanUpdateExternalPlaceholder(User user)
        => !user.IsActive && EduIdDirectoryKeys.IsEduId(user.ProviderKey);

    private static UserSearchResult CreateSearchResult(User user) => new(
        user.UserName,
        user.DisplayName,
        user.Email,
        UserSearchSources.Repository,
        user.ProviderKey,
        user.Organization);

    private async Task<EduIdExternalAccountResult> CreateExternalAccount(
        string email,
        string displayName,
        EduIdInviteDeliveryMode deliveryMode,
        CancellationToken ct)
    {
        var user = new User
        {
            UserName = email,
            DisplayName = displayName,
            Email = email,
            ProviderKey = EduIdDirectoryKeys.ProviderKey,
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
            SuppressSendingEmails = deliveryMode != EduIdInviteDeliveryMode.SendEmail,
            Invites = [email],
            RoleIdentifiers = [_options.RoleIdentifier],
            RoleExpiryDate = DateTime.Now.AddDays(_options.RoleExpiryDays),
            ExpiryDate = DateTime.Now.AddDays(_options.InvitationExpiryDays)
        };

        var response = await invitationClient.CreateInvitationAsync(request, ct);
        if (response.Status != (int)HttpStatusCode.OK && response.Status != (int)HttpStatusCode.Created)
            throw new InvalidOperationException($"Unexpected EduID invitation response status: {response.Status}.");

        var invitationUrl = response.RecipientInvitationUrls?
                                .FirstOrDefault(r =>
                                    string.Equals(r.Recipient, email, StringComparison.OrdinalIgnoreCase))
                                ?.InvitationUrl
                            ?? response.RecipientInvitationUrls?.FirstOrDefault()?.InvitationUrl;

        if (deliveryMode == EduIdInviteDeliveryMode.ReturnInvitationUrl && string.IsNullOrWhiteSpace(invitationUrl))
        {
            throw new EduIdInviteException(EduIdInviteFailureReason.MissingInvitationUrl,
                $"The EduID invitation response did not contain an invitation URL for '{email}'.");
        }

        await userRepository.Create(user, ct);
        logger.LogInformation("Created pending EduID user for {Email}", email);

        return new EduIdExternalAccountResult(EduIdExternalAccountStatus.Invited, user, invitationUrl);
    }
}