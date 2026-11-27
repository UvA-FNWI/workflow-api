using System.ComponentModel.DataAnnotations;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.WorkflowModel;

public class Screen : INamed, IDeclaredKeys
{
    /// <summary>
    /// Internal name of the screen
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Workflow definition whose instances this screen lists. Defaults to the owning definition.
    /// </summary>
    public string? WorkflowDefinition { get; set; }

    /// <summary>
    /// Parent-scoped related-screen marker; such screens are omitted from the register overview hub.
    /// </summary>
    public string? Relation { get; set; }

    /// <summary>
    /// User-facing title for overview navigation and the screen page heading.
    /// </summary>
    public BilingualString? Title { get; set; }

    /// <summary>
    /// Optional subtitle shown on the overview list.
    /// </summary>
    public BilingualString? Description { get; set; }

    /// <summary>
    /// Sort order when multiple screens from the same workflow appear on the overview.
    /// </summary>
    public int? Index { get; set; }

    /// <summary>
    /// Optional condition for showing this screen in overview navigation.
    /// </summary>
    public Condition? Condition { get; set; }

    /// <summary>
    /// List of columns to display on this screen
    /// </summary>
    public Column[] Columns { get; set; } = [];

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

    [YamlIgnore] public HashSet<string> DeclaredKeys { get; set; } = new();

    public Screen Clone() => new()
    {
        Name = Name,
        Columns = Columns.Select(column => column.Clone()).ToArray(),
        Grouping = Grouping is null
            ? null
            : new ScreenGrouping
            {
                Groups = Grouping.Groups.Select(group => new StepGroup
                {
                    Name = group.Name,
                    Title = group.Title,
                    Steps = [.. group.Steps],
                    Order = group.Order,
                    Enabled = group.Enabled
                }).ToArray()
            },
        BulkEdit = BulkEdit?.Clone(),
        BulkActions = BulkActions.Select(action => action.Clone()).ToArray(),
        DeclaredKeys = [.. DeclaredKeys]
    };
}

public class BulkAction
{
    [Required] public string Action { get; set; } = null!;
    [Required] public BilingualString Label { get; set; } = null!;
    [Required] public Icon Icon { get; set; } = null!;

    public BulkAction Clone() => (BulkAction)MemberwiseClone();
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
    /// Optional placement of the group
    /// </summary>
    public int? Order { get; set; }

    /// <summary>
    /// Optional flag to enable or disable the group
    /// </summary>
    public bool? Enabled { get; set; }

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

public enum ColumnType
{
    Normal,
    CurrentStep,
    RelatedUsers
}

public class Column : Field
{
    /// <summary>
    /// Internal name of the column, used for inheritance
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Type of the column
    /// </summary>
    public ColumnType Type { get; set; } = ColumnType.Normal;

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

    public Column Clone()
    {
        var clone = (Column)MemberwiseClone();
        clone.PropertyDefinition = null;
        clone.Event = null;
        return clone;
    }
}

public class BulkEditConfig : IDeclaredKeys
{
    public BulkEditProperty Identifier { get; set; } = null!;
    public BulkEditProperty[]? ReadOnlyProperties { get; set; } = [];
    public string[] EditableProperties { get; set; } = [];

    [YamlIgnore] public HashSet<string> DeclaredKeys { get; set; } = new();

    public BulkEditConfig Clone() => new()
    {
        Identifier = Identifier.Clone(),
        ReadOnlyProperties = ReadOnlyProperties?
            .Select(property => property.Clone()).ToArray(),
        EditableProperties = [.. EditableProperties],
        DeclaredKeys = [.. DeclaredKeys]
    };
}

public class BulkEditProperty
{
    public string Property { get; set; } = null!;
    public BilingualString? Title { get; set; }

    public BulkEditProperty Clone() => new()
    {
        Property = Property,
        Title = Title
    };
}