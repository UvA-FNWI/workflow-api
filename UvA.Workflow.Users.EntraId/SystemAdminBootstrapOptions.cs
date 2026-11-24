namespace UvA.Workflow.Users.EntraId;

/// <summary>
/// Break-glass SystemAdmin uids for Entra mode, where the directory does not supply roles.
/// This is not the function-assignment catalogue: a listed SurfConext uid receives the
/// SystemAdmin role, and nobody else does.
/// </summary>
public class SystemAdminBootstrapOptions
{
    public const string Section = "SystemAdmin";
    public const string RoleName = "SystemAdmin";

    /// <summary>Comma-separated SurfConext uids. Env: SystemAdmin__BootstrapUids.</summary>
    public string? BootstrapUids { get; set; }

    public bool IsListed(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(BootstrapUids))
            return false;

        var name = userName.Trim();
        return BootstrapUids
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(uid => uid.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}