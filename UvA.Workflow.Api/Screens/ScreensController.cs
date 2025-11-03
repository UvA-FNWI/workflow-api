using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Screens.Dtos;

namespace UvA.Workflow.Api.Screens;

public class ScreensController(ScreenDataService screenDataService) : ApiControllerBase
{
    /// <summary>
    /// Gets the specific screen for an instance, with column and rows
    /// </summary>
    /// <param name="entityType">The entity type to get instances for</param>
    /// <param name="screenName">The name of the screen configuration to use</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Screen data with columns and rows containing the projected data</returns>
    [HttpGet("{entityType}/{screenName}")]
    public async Task<ActionResult<ScreenDataDto>> GetScreenData(
        string entityType, 
        string screenName, 
        CancellationToken ct)
    {
        try
        {
            var screenData = await screenDataService.GetScreenData(screenName, entityType, ct);
            return Ok(screenData);
        }
        catch (ArgumentException ex)
        {
            return NotFound("ScreenNotFound", ex.Message);
        }
        catch (Exception ex)
        {
            return Problem(
                detail: ex.Message,
                title: "Error retrieving screen data"
            );
        }
    }
}
