namespace UvA.Workflow.Entities.Domain;

public class Screen
{
    /// <summary>
    /// Internal name of the screen
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// WorkflowDefinition this screen is used for
    /// </summary>
    public string? WorkflowDefinition { get; set; }

    /// <summary>
    /// List of columns to display on this screen
    /// </summary>
    public Column[] Columns { get; set; } = null!;

    /// <summary>
    /// Relation this screen is used for
    /// </summary>
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
    /// <summary>
    /// Determines whether this column can be used for filtering
    /// </summary>
    public FilterType FilterType { get; set; }

    /// <summary>
    /// Determines whether this column is visible in the user interface or only in the export 
    /// </summary>
    public DisplayType DisplayType { get; set; }

    /// <summary>
    /// Sets a default sort direction for this column
    /// </summary>
    public SortDirection? DefaultSort { get; set; }

    /// <summary>
    /// If true, this column becomes a link to the instance 
    /// </summary>
    public bool Link { get; set; }
}