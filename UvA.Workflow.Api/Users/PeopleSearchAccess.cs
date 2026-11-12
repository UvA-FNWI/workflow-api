using UvA.Workflow.Api.Personal;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Users;

public class PeopleSearchAccess(
    IUserService userService,
    RightsService rightsService,
    ModelService modelService,
    IWorkflowInstanceRepository workflowInstanceRepository)
{
    public async Task<bool> CanSearch(CancellationToken ct)
    {
        var user = await userService.GetCurrentUser(ct);
        return user != null && await CanSearch(user, await userService.GetRolesOfCurrentUser(ct), ct);
    }

    public async Task<bool> CanSearch(User user, IEnumerable<string> roles, CancellationToken ct)
    {
        if (rightsService.Can(roles, RoleAction.ViewAdminTools))
            return true;

        if (!ObjectId.TryParse(user.Id, out var userId))
            return false;

        // A coordinator or local admin can be assigned through a user property on an instance.
        // The role can also apply to instances that reference the assignment through inheritance.
        var filter = LinkedUserInstances.BuildFilter(modelService.WorkflowDefinitions.Values, [userId],
            definition => LinkedUserInstances.GetUserProperties(definition).Where(property =>
                definition.Roles.GetOrDefault(property.Name)?.Actions.Any(action =>
                    action.Type == RoleAction.ViewAdminTools) == true));
        if (filter == null)
            return false;

        foreach (var instance in await workflowInstanceRepository.GetByFilter(filter, ct))
        {
            ct.ThrowIfCancellationRequested();
            if (await rightsService.Can(instance, RoleAction.ViewAdminTools))
                return true;
        }

        return false;
    }
}