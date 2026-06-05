namespace UvA.Workflow.Users;

public enum ExternalUserCreationFailureReason
{
    InvalidEmailAddress,
    InternalEmailAddress,
    UserAlreadyExists
}

public class ExternalUserCreationException(
    ExternalUserCreationFailureReason reason,
    string message) : InvalidOperationException(message)
{
    public ExternalUserCreationFailureReason Reason { get; } = reason;
}

public interface IExternalUserService
{
    Task<UserSearchResult> CreateOrUpdateExternalUser(
        string displayName,
        string email,
        Organization? organization,
        CancellationToken ct = default);
}