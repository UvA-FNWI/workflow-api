using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.WorkflowDefinitions.Dtos;

public record ScreenSummaryDto(
    string Name,
    BilingualString? Title,
    BilingualString? Description,
    int? Index)
{
    public static ScreenSummaryDto Create(Screen screen) =>
        new(screen.Name, screen.Title, screen.Description, screen.Index);
}