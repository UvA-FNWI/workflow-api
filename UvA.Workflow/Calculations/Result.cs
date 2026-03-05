namespace UvA.Workflow.Calculations;

public class Result
{
    /// <summary>
    /// The question this result belongs to
    /// </summary>
    public string QuestionName { get; set; }

    /// <summary>
    /// The page the question is on
    /// </summary>
    public string PageName { get; set; }

    /// <summary>
    /// The weight of the question
    /// </summary>
    public int Weight { get; set; }

    /// <summary>
    /// The given answer
    /// </summary>
    public int Answer { get; set; }
}