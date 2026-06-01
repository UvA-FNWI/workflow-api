namespace UvA.Workflow.WorkflowModel;

public class RelatedUser
{
    public string Property { get; set; } = null!;

    public string Group { get; set; }

    public BilingualString? Text { get; set; } = null;

    [YamlIgnore] public PropertyDefinition? PropertyDefinition { get; set; }

    public BilingualString DisplayTitle => Text ?? PropertyDefinition?.DisplayName ?? Property;
}

/// <summary>
/// Defines a named group of related users
/// </summary>
public class RelatedUserGroup
{
    /// <summary>
    /// Internal identifier for the group
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Display name of the group
    /// </summary>
    public BilingualString Title { get; set; } = null!;
}

/// <summary>
/// Configuration for grouping related users
/// </summary>
public class RelatedUserGrouping
{
    public RelatedUserGroup[] Groups { get; set; } = [];
}