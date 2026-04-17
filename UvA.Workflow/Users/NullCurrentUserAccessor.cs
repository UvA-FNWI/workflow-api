namespace UvA.Workflow.Users;

public class NullCurrentUserAccessor : ICurrentUserAccessor
{
    public string? GetCurrentUserName() => null;
}