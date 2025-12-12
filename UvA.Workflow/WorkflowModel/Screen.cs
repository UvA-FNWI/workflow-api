namespace UvA.Workflow.Entities.Domain;

public class Screen : INamed
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

    /// <summary>
    /// Optional grouping configuration for grouping screen data by steps
    /// </summary>
    public ScreenGrouping? Grouping { get; set; }
}

/// <summary>
/// Configuration for grouping screen data by workflow steps
/// </summary>
public class ScreenGrouping
{
    /// <summary>
    /// List of step groups
    /// </summary>
    public StepGroup[] Groups { get; set; } = [];
}

/// <summary>
/// Defines a group of workflow steps
/// </summary>
public class StepGroup
{
    /// <summary>
    /// Internal identifier for the group
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Display name of the group in nl/en
    /// </summary>
    public BilingualString Title { get; set; } = null!;

    /// <summary>
    /// Array of step names that belong to this group
    /// </summary>
    public string[] Steps { get; set; } = [];
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