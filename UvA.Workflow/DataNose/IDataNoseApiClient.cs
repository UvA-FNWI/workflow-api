namespace UvA.Workflow.DataNose;

public interface IDataNoseApiClient
{
    Task<IEnumerable<string>> GetRolesByUser(string userId, CancellationToken ct = default);
    
    Task<IEnumerable<ExternalUser>> SearchPeople(string query, CancellationToken ct = default);
}