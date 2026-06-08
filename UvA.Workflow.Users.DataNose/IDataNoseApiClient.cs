using UvA.Workflow.Organizations;

namespace UvA.Workflow.Users.DataNose;

public interface IDataNoseApiClient
{
    Task<IEnumerable<string>> GetRolesByUser(string userId, CancellationToken ct = default);

    Task<IEnumerable<UserSearchResult>> SearchPeople(string query, CancellationToken ct = default);

    Task<Organization?> GetOrganizationForUser(string uid, CancellationToken ct = default);
}