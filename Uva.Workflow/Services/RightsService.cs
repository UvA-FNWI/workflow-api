using Uva.Workflow.Users;

namespace Uva.Workflow.Services;

public class RightsService
{
    private readonly ExternalUser _user = new ExternalUser("1", "User 1", "1@invalid.invalid");

    public Task<ExternalUser> GetUser() => Task.FromResult(_user);
    public Task<string> GetUserId()
    {
        return Task.FromResult(_user.ExternalId); // Hardcode for now
    }
}