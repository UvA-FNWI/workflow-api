namespace UvA.Workflow.Users;

public enum ExternalUserCreationFailureReason
{
    InvalidEmailAddress,
    InternalEmailAddress,
    UserAlreadyExists,
    ExternalUsersNotAllowed,
    InvalidQuestionType,
    InvalidUserId
}

public record ExternalUserInput(
    string DisplayName,
    string Email,
    Organization? Organization = null,
    string? UserId = null);

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
        string? userId,
        CancellationToken ct = default);
}