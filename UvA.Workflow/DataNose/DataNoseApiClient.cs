using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace UvA.Workflow.DataNose;

public class DataNoseApiClient(IHttpClientFactory httpFactory) : IDataNoseApiClient
{
    public const string Name = "DataNoseApiClient";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IEnumerable<string>> GetRolesByUser(string userId, CancellationToken ct = default)
    {
        var url = QueryHelpers.AddQueryString(
            "api/Common/Roles/GetRolesForUser",
            new Dictionary<string, string?>
            {
                ["Uid"] = userId
            });
        var http = httpFactory.CreateClient(Name);
        var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"GetRolesForUser failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
        }

        var dto = await response.Content.ReadFromJsonAsync<GetRolesByUserResponse>(JsonOptions, ct);
        if (dto is null)
            throw new Exception($"Failed to deserialize response for GetRolesForUser: {response.Content}");
        return dto.Roles.Select(r => r.Name).Distinct();
    }

    public async Task<IEnumerable<ExternalUser>> SearchPeople(string query, CancellationToken ct = default)
    {
        var url = QueryHelpers.AddQueryString(
            "api/Common/Search/SearchPeople",
            new Dictionary<string, string?>
            {
                ["SearchTerm"] = query.Trim(),
                ["IncludeEmployees"] = "true",
                ["IncludeStudents"] = "true"
            });
        var http = httpFactory.CreateClient(Name);
        var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"SearchPeople failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
        }

        var peopleEntries = await response.Content.ReadFromJsonAsync<PeopleIndexEntry[]>(JsonOptions, ct);
        if (peopleEntries is null)
            throw new Exception($"Failed to deserialize response for SearchPeople: {response.Content}");

        return peopleEntries.Where(p =>
                (!string.IsNullOrEmpty(p.EmployeeUvAnetId) || !string.IsNullOrEmpty(p.StudentId)) &&
                !string.IsNullOrEmpty(p.Email))
            .Select(p => new ExternalUser((p.EmployeeUvAnetId ?? p.StudentId)!, p.FullName, p.Email!));
    }

    #region DTO

    // ReSharper disable ClassNeverInstantiated.Local

    private record GetRolesByUserResponse(GetRolesByUserResponse.RoleDto[] Roles)
    {
        public record RoleDto(string Name, string? DepartmentCode);
    }

    private record PeopleIndexEntry(
        int? Id,
        string? StudentId,
        string? EmployeeUvAnetId,
        string? Email,
        string Department,
        int? StaffId,
        bool IsActiveEmployee,
        bool IsActiveStudent,
        bool IsExternalStaff,
        bool IsExternalRepresentative,
        string FullName,
        bool? HasMultipleLogins);

    // ReSharper restore ClassNeverInstantiated.Local

    #endregion
}