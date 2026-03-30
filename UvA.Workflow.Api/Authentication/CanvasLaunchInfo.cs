using System.Text.Json;
using UvA.LTI;

namespace UvA.Workflow.Api.Authentication;

public record CanvasLaunchInfo(
    string UvanetId,
    string DisplayName,
    string Email,
    string[] CourseIdentifiers,
    bool IsTeacher,
    string Locale)
{
    private const string InstructorRole = "http://purl.imsglobal.org/vocab/lis/v2/membership#Instructor";

    public static CanvasLaunchInfo FromPrincipal(LtiPrincipal principal)
    {
        var uvanetId = ExtractUvanetId(principal)
                       ?? throw new InvalidOperationException("Canvas launch is missing the UvAnetID.");
        var displayName = principal.Name?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = uvanetId;

        var email = principal.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email))
            email = $"{uvanetId}@invalid.uva.nl";

        var courseIdentifiers = GetCourseIdentifiers(principal)
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => i!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CanvasLaunchInfo(
            uvanetId,
            displayName,
            email,
            courseIdentifiers,
            HasTeacherRole(principal.Roles),
            principal.Locale ?? "en");
    }

    private static bool HasTeacherRole(IEnumerable<string>? roles)
        => roles?.Any(role => role.Contains(InstructorRole, StringComparison.OrdinalIgnoreCase)) == true;

    private static IEnumerable<string?> GetCourseIdentifiers(LtiPrincipal principal)
    {
        yield return principal.Lis?.Course_offering_sourcedid;
        yield return principal.Context.Id;

        if (principal.CustomClaims is { ValueKind: JsonValueKind.Object } customClaims &&
            customClaims.TryGetProperty("courseid", out var courseId))
        {
            yield return courseId.ToString();
        }
    }

    private static string? ExtractUvanetId(LtiPrincipal principal)
    {
        var lisId = principal.Lis?.Person_sourcedid;
        if (!string.IsNullOrWhiteSpace(lisId))
        {
            var value = lisId.Split('@', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        if (!string.IsNullOrWhiteSpace(principal.NameIdentifier))
            return principal.NameIdentifier.Trim();

        if (!string.IsNullOrWhiteSpace(principal.Email))
            return principal.Email.Split('@', StringSplitOptions.RemoveEmptyEntries)[0].Trim();

        return null;
    }
}