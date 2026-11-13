using UvA.Workflow.Api.Personal;
using UvA.Workflow.Api.Users.Dtos;

namespace UvA.Workflow.Api.Users;

public class PeopleSearchService(
    IUserRepository userRepository,
    LinkedUserInstances linkedUserInstances)
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
            people.AddRange(await GetVisiblePeople(users, ct));
            if (people.Count > ResultLimit)
                break;
        }

        return new PeopleSearchDto(people.Take(ResultLimit).ToArray(), people.Count > ResultLimit);
    }

    private async Task<IEnumerable<PersonDto>> GetVisiblePeople(User[] users, CancellationToken ct)
    {
        var candidates = new List<(User User, ObjectId Id)>();
        foreach (var user in users)
            if (ObjectId.TryParse(user.Id, out var id))
                candidates.Add((user, id));

        var matches = await linkedUserInstances.FindVisibleLinks(
            candidates.Select(candidate => candidate.Id).ToArray(), ct, stopWhenAllUsersFound: true);
        var visibleIds = matches.SelectMany(match => match.Users).Select(link => link.Id).ToHashSet();

        return candidates
            .Where(candidate => visibleIds.Contains(candidate.Id))
            .Select(candidate => PersonDto.Create(candidate.User));
    }
}