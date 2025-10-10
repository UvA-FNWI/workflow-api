namespace UvA.Workflow.Entities.Domain;

public class Screen
{
    public string Name { get; set; } = null!;
    public string? EntityType { get; set; }
    public Column[] Columns { get; set; } = null!;
    public string? Relation { get; set; }
}

public enum FilterType
{
    None,
    Pick
}

public enum DisplayType
{
    Normal,
    ExportOnly
}

public enum SortDirection
{
    Ascending,
    Descending
}

public class Column : Field
{
    public FilterType FilterType { get; set; }
    public DisplayType DisplayType { get; set; }
    public SortDirection? DefaultSort { get; set; }

    public bool Link { get; set; }
}