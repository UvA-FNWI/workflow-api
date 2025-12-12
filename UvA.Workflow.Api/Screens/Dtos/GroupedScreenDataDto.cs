namespace UvA.Workflow.Api.Screens.Dtos;

public record GroupedScreenDataDto(
    string Name,
    string EntityType,
    ScreenColumnDto[] Columns,
    ScreenGroupDto[] Groups);

public record ScreenGroupDto(
    string Name,
    BilingualString Title,
    ScreenRowDto[] Rows);