using UvA.Workflow.Api.Personal;
using UvA.Workflow.Api.Users.Dtos;

namespace UvA.Workflow.Api.Users;

public class PeopleSearchService(
    IUserRepository userRepository,
    IWorkflowInstanceRepository workflowInstanceRepository,
    ModelService modelService,
    RightsService rightsService)
{
    private const int ResultLimit = 50;

    public async Task<PeopleSearchDto> Search(string? query, CancellationToken ct)
    {
        var term = query?.Trim();
        if (term is not { Length: >= 2 and <= 200 })
            return new PeopleSearchDto([], false);

        var people = new List<PersonDto>();
        await foreach (var users in userRepository.SearchByPrefix(term, ct).Chunk(ResultLimit).WithCancellation(ct))
        {
            var visibleIds = await GetVisibleLinkedIds(users, ct);
            people.AddRange(users
                .Where(user => ObjectId.TryParse(user.Id, out var id) && visibleIds.Contains(id))
                .Select(PersonDto.Create));
            if (people.Count > ResultLimit)
                break;
        }

        return new PeopleSearchDto(people.Take(ResultLimit).ToArray(), people.Count > ResultLimit);
    }

    private async Task<HashSet<ObjectId>> GetVisibleLinkedIds(User[] users, CancellationToken ct)
    {
        var remainingIds = new HashSet<ObjectId>();
        foreach (var user in users)
            if (ObjectId.TryParse(user.Id, out var id))
                remainingIds.Add(id);

        var definitions = modelService.WorkflowDefinitions;
        var filter = LinkedUserInstances.BuildFilter(definitions.Values, remainingIds);
        var visibleIds = new HashSet<ObjectId>();
        if (filter == null)
            return visibleIds;

        foreach (var instance in await workflowInstanceRepository.GetByFilter(filter, ct))
        {
            ct.ThrowIfCancellationRequested();
            var linkedIds = LinkedUserInstances.GetUserIds(instance, definitions[instance.WorkflowDefinition])
                .Where(remainingIds.Contains).ToArray();
            if (linkedIds.Length == 0 || !await rightsService.Can(instance, RoleAction.View))
                continue;

            visibleIds.UnionWith(linkedIds);
            remainingIds.ExceptWith(linkedIds);
            if (remainingIds.Count == 0)
                break;
        }

        return visibleIds;
    }
}