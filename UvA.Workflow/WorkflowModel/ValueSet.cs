namespace UvA.Workflow.WorkflowModel;

public class ValueSet : INamed
{
    public string Name { get; set; } = null!;

    /// <summary>
    /// Dictionary of choices within this value set
    /// </summary>
    public List<Choice> Values { get; set; } = null!;

    /// <summary>
    /// Determines how the choices should be sorted when shown in a selector
    /// </summary>
    public ValueSetSorting? Sorting { get; set; }
}

public enum ChoiceSortField
{
    Name,
    Text,
    Value,
    Description
}

public class ValueSetSorting
{
    /// <summary>
    /// Field of the choice to sort on
    /// </summary>
    public ChoiceSortField Field { get; set; }

    /// <summary>
    /// Direction to sort in
    /// </summary>
    public SortDirection Direction { get; set; }
}