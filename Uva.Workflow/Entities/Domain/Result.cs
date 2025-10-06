namespace Uva.Workflow.Entities.Domain;

public class Result
{
    public BilingualString Title { get; set; } = null!;
    public string[] Questions { get; set; } = [];
    public double Weight { get; set; }
}