namespace UvA.Workflow.Entities.Domain;

public class ValueSet : INamed
{
    public string Name { get; set; }

    /// <summary>
    /// Dictionary of choices within this value set
    /// </summary>
    public Dictionary<string, Choice> Values { get; set; } = null!;
}