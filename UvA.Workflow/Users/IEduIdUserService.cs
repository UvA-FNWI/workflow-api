namespace UvA.Workflow.Users;

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

public enum EduIdInviteDeliveryMode
{
    SendEmail,
    ReturnInvitationUrl
}

public enum EduIdExternalAccountStatus
{
    Invited,
    AlreadyActive,
    PendingInvitation,
    InternalEmail
}

public record EduIdExternalAccountResult(
    EduIdExternalAccountStatus Status,
    User? User = null,
    string? InvitationUrl = null);

public record EduIdUserInviteResult(User User, string InvitationUrl);

public interface IEduIdUserService
{
    bool IsInternalEmailAddress(string email);

    Task<EduIdUserInviteResult> InviteUser(string email, string displayName, CancellationToken ct = default);

    Task<EduIdExternalAccountResult> EnsureExternalAccount(
        string email,
        string displayName,
        EduIdInviteDeliveryMode deliveryMode,
        CancellationToken ct = default);

    Task<User?> ResolveAuthenticatedUser(string uid, string displayName, string? email, CancellationToken ct = default);
}