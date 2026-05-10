namespace UvA.Workflow.Users;

public interface ICurrentUserAccessor
{
    string? GetCurrentUserName();
}