using System.ComponentModel.DataAnnotations;
using UvA.Workflow.Expressions;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.WorkflowModel;

public class Screen : INamed
{
    /// <summary>
    /// Internal name of the screen
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// List of columns to display on this screen
    /// </summary>
    public Column[] Columns { get; set; } = null!;

    /// <summary>
    /// Relation this screen is used for
    /// </summary>
    public string? Relation { get; set; }

    /// <summary>
    /// Optional grouping configuration for screen data
    /// </summary>
    public ScreenGrouping? Grouping { get; set; }

    /// <summary>
    /// Config for bulk editing for all instances in the screen
    /// </summary>
    public BulkEditConfig? BulkEdit { get; set; }

    /// <summary>
    /// Ordered buttons referencing existing instance Execute actions
    /// </summary>
    public BulkAction[] BulkActions { get; set; } = [];
}

public class BulkAction
{
    [Required] public string Action { get; set; } = null!;
    [Required] public BilingualString Label { get; set; } = null!;
    [Required] public Icon Icon { get; set; } = null!;
}

/// <summary>
/// Configuration for grouping screen data by conditions or workflow steps
/// </summary>
public class ScreenGrouping
{
    /// <summary>
    /// List of screen groups in display order
    /// </summary>
    public StepGroup[] Groups { get; set; } = [];
}

/// <summary>
/// Defines a group of workflow instances
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
    /// Step names that belong to this group when it has no condition
    /// </summary>
    public string?[] Steps { get; set; } = [];

    /// <summary>
    /// Optional condition evaluated before step groups. The first matching condition wins.
    /// </summary>
    public Condition? Condition { get; set; }
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

    /// <summary>
    /// Display overrides in priority order. The first matching condition supplies the cell text and color.
    /// </summary>
    public ColumnDisplayOverride[] Overrides { get; set; } = [];

    [YamlIgnore] public IEnumerable<Lookup> Lookups => Properties.Concat(Overrides.SelectMany(rule => rule.Lookups));
}

public class ColumnDisplayOverride
{
    [Required] public Condition Condition { get; set; } = null!;
    [Required] public BilingualString Text { get; set; } = null!;
    public StatusColor? Color { get; set; }

    [YamlIgnore] public BilingualTemplate TextTemplate => field ??= BilingualTemplate.Create(Text)!;

    [YamlIgnore] public IEnumerable<Lookup> Lookups => Condition.Properties.Concat(TextTemplate.Properties);
}

public class BulkEditConfig
{
    public BulkEditProperty Identifier { get; set; } = null!;
    public BulkEditProperty[]? ReadOnlyProperties { get; set; } = [];
    public string[] EditableProperties { get; set; } = [];
}

public class BulkEditProperty
{
    public string Property { get; set; } = null!;
    public BilingualString? Title { get; set; }
}