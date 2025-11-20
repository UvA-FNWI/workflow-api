namespace UvA.Workflow.DataNose;

public record UserInfo(string UserName, string DisplayName, string Email);

public interface IDataNoseApiClient
{
    Task<IEnumerable<string>> GetRolesByUser(string userId, CancellationToken ct = default);

    Task<IEnumerable<UserInfo>> SearchPeople(string query, CancellationToken ct = default);
}