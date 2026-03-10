namespace UvA.Workflow.Assessments;

public class Result
{
    /// <summary>
    /// The question this result belongs to
    /// </summary>
    public string QuestionName { get; set; } = null!;

    /// <summary>
    /// The weight of the question
    /// </summary>
    public int Weight { get; set; }

    /// <summary>
    /// The percentage of the weight of the answer in relation to all other questions in the form
    /// </summary>
    public double Percentage { get; set; }

    /// <summary>
    /// The given answer
    /// </summary>
    public int Answer { get; set; }
}