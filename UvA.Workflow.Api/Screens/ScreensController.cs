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

    [HttpGet("Projects/Overview")]
    public async Task<ActionResult<GroupedScreenDataDto>> GetProjectsOverview(CancellationToken ct)
    {
        try
        {
            var screenData = await screenDataService.GetScreenData(
                "Projects",
                "Project-AI",
                ct);

            var grouped = GroupByCurrentStep(screenData);
            return Ok(grouped);
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

    private GroupedScreenDataDto GroupByCurrentStep(ScreenDataDto screenData)
    {
        const string groupAssignSubject = "assign-subject";
        const string groupThesisInProgress = "thesis-in-progress";
        const string groupCompleted = "completed";

        Dictionary<string, string> stepGroupMapping = new(StringComparer.OrdinalIgnoreCase)
        {
            // Subject assignment
            ["Start"] = groupAssignSubject,
            ["Subject"] = groupAssignSubject,
            ["SubjectFeedback"] = groupAssignSubject,

            // Thesis in progress
            ["Proposal"] = groupThesisInProgress,
            ["Upload"] = groupThesisInProgress,
            ["Assessment"] = groupThesisInProgress,
            ["AssessmentReviewer"] = groupThesisInProgress,
            ["AssessmentSupervisor"] = groupThesisInProgress,
            ["ApprovalCoordinator"] = groupThesisInProgress,

            // Completed
            ["Publication"] = groupCompleted
        };

        var currentStepColumn = screenData.Columns.FirstOrDefault(c => c.IsCurrentStep);
        if (currentStepColumn is null)
        {
            return new GroupedScreenDataDto
            {
                ["Ungrouped"] = screenData
            };
        }

        var currentStepId = currentStepColumn.Id;

        // Rebuild columns without current step and reindex IDs to keep them compact
        var columnsWithoutStep = screenData.Columns
            .Where(c => !c.IsCurrentStep)
            .Select((c, idx) => c with { Id = idx })
            .ToArray();

        var idMap = screenData.Columns
            .Where(c => !c.IsCurrentStep)
            .Select((c, idx) => (OldId: c.Id, NewId: idx))
            .ToDictionary(x => x.OldId, x => x.NewId);

        var grouped = new GroupedScreenDataDto();

        foreach (var row in screenData.Rows)
        {
            var stepValue = row.Values.TryGetValue(currentStepId, out var stepObj)
                ? stepObj?.ToString() ?? "Draft"
                : "Draft";

            var groupName = stepGroupMapping.TryGetValue(stepValue, out var mapped)
                ? mapped
                : stepValue;

            var newValues = new Dictionary<int, object?>();
            foreach (var kvp in row.Values)
            {
                if (kvp.Key == currentStepId)
                    continue;

                if (idMap.TryGetValue(kvp.Key, out var newId))
                {
                    newValues[newId] = kvp.Value;
                }
            }

            if (!grouped.TryGetValue(groupName, out var groupScreen))
            {
                groupScreen = screenData with { Columns = columnsWithoutStep, Rows = [] };
                grouped[groupName] = groupScreen;
            }

            var rowsList = grouped[groupName].Rows.ToList();
            rowsList.Add(row with { Values = newValues });
            grouped[groupName] = grouped[groupName] with { Rows = rowsList.ToArray() };
        }

        return grouped;
    }
}