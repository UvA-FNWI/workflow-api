using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Settings.Dtos;

namespace UvA.Workflow.Api.Settings;

public class SettingsController(ResolvedWorkflowConfig config) : ApiControllerBase
{
    /// <summary>
    /// Returns deployment settings from workflow-config Settings.yaml (institution branding and UI copy).
    /// </summary>
    [HttpGet]
    public ActionResult<SettingsDto> Get()
    {
        var dto = SettingsDto.Create(config.Settings);
        return dto == null ? NotFound("SettingsNotFound", "No Settings.yaml found for this deployment.") : Ok(dto);
    }
}