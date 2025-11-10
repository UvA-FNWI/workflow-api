namespace UvA.Workflow.Entities.Domain;

public class Result
{
    /// <summary>
    /// Localized title of this result shown to the user
    /// </summary>
    public BilingualString Title { get; set; } = null!;
    /// <summary>
    /// List of questions that are included in this result entry
    /// </summary>
    public string[] Questions { get; set; } = [];
    /// <summary>
    /// Weight of this result entry
    /// </summary>
    public double Weight { get; set; }
}