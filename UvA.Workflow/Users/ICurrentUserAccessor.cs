namespace UvA.Workflow.Users;

public interface ICurrentUserAccessor
{
    /// <summary>
    /// Returns the name of the (impersonated) current user
    /// </summary>
    string? GetCurrentUserName();

    /// <summary>
    /// Returns the name of the current real user, ignoring impersonation
    /// </summary>
    /// <returns></returns>
    string? GetRealUserName() => GetCurrentUserName();
}