namespace Uva.Workflow.Services;

public class RightsService
{
    private readonly ExternalUser _user = new ExternalUser("1", "User 1", "1@invalid.invalid");

    public Task<ExternalUser> GetUser() => Task.FromResult(_user);

    public Task<string> GetUserId()
    {
        return Task.FromResult(_user.Id); // Hardcode for now
    }

    public Task<bool> Can(WorkflowInstance instance, RoleAction action, string? formName = null)
    {
        return action switch
        {
            RoleAction.Edit => Task.FromResult(true),
            RoleAction.Submit => Task.FromResult(true),
            RoleAction.ViewHidden => Task.FromResult(true),
            _ => Task.FromResult(false)
        };
    }
}