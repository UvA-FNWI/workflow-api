using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using MongoDB.Bson;
using UvA.Workflow.Organizations;

namespace UvA.Workflow.Users.DataNose;

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

    public async Task<IEnumerable<UserSearchResult>> SearchPeople(string query, CancellationToken ct = default)
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
            .Select(p => new UserSearchResult((p.EmployeeUvAnetId ?? p.StudentId)!,
                p.FullName,
                p.Email!,
                DataNoseDirectoryKeys.SourceKey,
                Organization: CreateOrganization(p.Department)));
    }

    public async Task<Organization?> GetOrganizationForUser(string uid, CancellationToken ct = default)
    {
        var url = QueryHelpers.AddQueryString(
            "api/Common/People/GetOrganizationForUser",
            new Dictionary<string, string?>
            {
                ["Uid"] = uid
            });
        var http = httpFactory.CreateClient(Name);
        var response = await http.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"GetOrganizationForUser failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
        }

        var dto = await response.Content.ReadFromJsonAsync<GetOrganizationForUserResponse>(JsonOptions, ct);

        return CreateOrganization(dto?.Department);
    }

    /// <summary>
    /// Builds an <see cref="Organization"/> from the DataNose department code (e.g. "FNWI/CoI", "FEB").
    /// Returns null when no department is known.
    /// </summary>
    private static Organization? CreateOrganization(string? department)
        => string.IsNullOrWhiteSpace(department)
            ? null
            : Organization.Create(department.Trim());

    #region DTO

    // ReSharper disable ClassNeverInstantiated.Local

    private record GetRolesByUserResponse(GetRolesByUserResponse.RoleDto[] Roles)
    {
        public record RoleDto(string Name, string? DepartmentCode);
    }

    private record GetOrganizationForUserResponse(string? Department);

    private record PeopleIndexEntry(
        int? Id,
        string? StudentId,
        string? EmployeeUvAnetId,
        string? Email,
        string? Department,
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