namespace UvA.Workflow.Api.Screens.Dtos;

public record ScreenDataDto(
    string Name,
    string EntityType,
    ScreenColumnDto[] Columns,
    ScreenRowDto[] Rows)
{
    public static ScreenDataDto Create(Screen screen, ScreenColumnDto[] columns, ScreenRowDto[] rows)
    {
        return new ScreenDataDto(
            screen.Name,
            screen.EntityType ?? "",
            columns,
            rows);
    }
}

public record ScreenColumnDto(
    int Id,
    BilingualString Title,
    string? Property,
    FilterType FilterType,
    DisplayType DisplayType,
    UvA.Workflow.Entities.Domain.SortDirection? DefaultSort,
    bool Link)
{
    public static ScreenColumnDto Create(Column column, int id)
    {
        return new ScreenColumnDto(
            id,
            column.DisplayTitle,
            column.Property,
            column.FilterType,
            column.DisplayType,
            column.DefaultSort,
            column.Link);
    }
}

public record ScreenRowDto(
    string Id,
    Dictionary<int, object?> Values)
{
    public static ScreenRowDto Create(string id, Dictionary<int, object?> values)
    {
        return new ScreenRowDto(id, values);
    }
}