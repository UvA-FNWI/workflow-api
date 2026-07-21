using Microsoft.AspNetCore.Authorization;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Api.Infrastructure;

namespace UvA.Workflow.Api.Versions;

[ApiExplorerSettings(IgnoreApi = true)]
[Authorize(AuthenticationSchemes = WorkflowAuthenticationDefaults.AnyScheme)]
public class VersionsController(
    ModelServiceResolver modelServiceResolver,
    WorkflowConfigLoader configLoader,
    RightsService rightsService,
    ILogger<VersionsController> logger)
    : ApiControllerBase
{
    private const string ProjectsPrefix = "Projects/";
    private const string LayoutPath = "Layouts/default.html";

    /// Reload the default version from the configured source.
    [HttpPost("reload")]
    public async Task<ActionResult> Reload()
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);
        try
        {
            await configLoader.LoadBaselineAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reloading baseline config");
            return BadRequest(ex.Message);
        }

        return Ok();
    }

    /// Load a branch as a named preview version; send it back in the Workflow-Version header to view it.
    [HttpPost("branch")]
    public async Task<ActionResult<string>> LoadBranch([FromQuery] string @ref)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);
        if (string.IsNullOrWhiteSpace(@ref))
            return BadRequest("ref is required");
        try
        {
            await configLoader.LoadBranchAsync(@ref);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading branch {Ref}", @ref);
            return BadRequest(ex.Message);
        }

        return Ok(@ref);
    }

    /// Upload checkout-relative Projects/ files and Layouts/default.html as a named preview version.
    [HttpPost("{version}")]
    public async Task<ActionResult> CreateVersion(string version, [FromBody] Dictionary<string, string> files)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);
        try
        {
            var normalizedFiles = files.ToDictionary(
                entry => entry.Key.Replace('\\', '/').Trim('/'),
                entry => entry.Value,
                StringComparer.Ordinal);
            if (!normalizedFiles.TryGetValue(LayoutPath, out var layout) || string.IsNullOrWhiteSpace(layout))
                return BadRequest($"Uploaded version must provide {LayoutPath}");

            var projectFiles = normalizedFiles
                .Where(entry => entry.Key.StartsWith(ProjectsPrefix, StringComparison.Ordinal))
                .ToDictionary(entry => entry.Key[ProjectsPrefix.Length..], entry => entry.Value,
                    StringComparer.Ordinal);
            if (projectFiles.Count == 0)
                return BadRequest($"Uploaded version must provide files under {ProjectsPrefix}");

            var parser = new ModelParser(new DictionaryProvider(projectFiles));
            modelServiceResolver.AddOrUpdate(version, parser, layout);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing model for version {Version}", version);
            return BadRequest(ex.Message);
        }

        logger.LogInformation("Installed uploaded config version {Version}", version);
        return Ok();
    }

    /// Version names as a string[] (consumed by the workflow-ui version selector).
    [HttpGet]
    public ActionResult<IEnumerable<string>> GetVersions()
        => Ok(modelServiceResolver.GetVersions().Select(v => v.Name));

    /// Version names plus provenance (commit + load time).
    [HttpGet("details")]
    public ActionResult<IReadOnlyCollection<VersionInfo>> GetVersionDetails()
        => Ok(modelServiceResolver.GetVersions());
}