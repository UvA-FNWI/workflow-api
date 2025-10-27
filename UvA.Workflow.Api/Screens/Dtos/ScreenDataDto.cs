namespace UvA.Workflow.Api.Screens.Dtos;

public class ScreenDataDto
{
    public string Name { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public ScreenColumnDto[] Columns { get; set; } = null!;
    public ScreenRowDto[] Rows { get; set; } = null!;

    public static ScreenDataDto Create(Screen screen, ScreenColumnDto[] columns, ScreenRowDto[] rows)
    {
        return new ScreenDataDto
        {
            Name = screen.Name,
            EntityType = screen.EntityType ?? "",
            Columns = columns,
            Rows = rows
        };
    }
}

public class ScreenColumnDto
{
    public int Id { get; set; }
    public BilingualString Title { get; set; } = null!;
    public string? Property { get; set; }
    public FilterType FilterType { get; set; }
    public DisplayType DisplayType { get; set; }
    public UvA.Workflow.Entities.Domain.SortDirection? DefaultSort { get; set; }
    public bool Link { get; set; }

    public static ScreenColumnDto Create(Column column, int id)
    {
        return new ScreenColumnDto
        {
            Id = id,
            Title = column.DisplayTitle,
            Property = column.Property,
            FilterType = column.FilterType,
            DisplayType = column.DisplayType,
            DefaultSort = column.DefaultSort,
            Link = column.Link
        };
    }
}

public class ScreenRowDto
{
    public string Id { get; set; } = null!;
    public Dictionary<int, object?> Values { get; set; } = null!;

    public static ScreenRowDto Create(string id, Dictionary<int, object?> values)
    {
        return new ScreenRowDto
        {
            Id = id,
            Values = values
        };
    }
}
