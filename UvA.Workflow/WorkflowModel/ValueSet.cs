namespace UvA.Workflow.Entities.Domain;

public class ValueSet : INamed
{
    public string Name { get; set; } = null!;

    /// <summary>
    /// Dictionary of choices within this value set
    /// </summary>
    public List<Choice> Values { get; set; } = null!;
}