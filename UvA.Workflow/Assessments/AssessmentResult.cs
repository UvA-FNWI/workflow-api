namespace UvA.Workflow.Assessments;

public class AssessmentPartResult
{
    public string PartName { get; set; } = null!;

    public decimal Score { get; set; }

    public List<SourceResult> SourceResults { get; set; } = [];
}

public class SourceResult
{
    public string SourceName { get; set; } = null!;
    public decimal Score { get; set; }
    public List<PageResult> PageResults { get; set; } = [];
}

public class PageResult
{
    public string PageName { get; set; } = null!;
    public decimal Weight { get; set; }
    public decimal WeightedAverage { get; set; }
    public List<QuestionResult> QuestionResults { get; set; } = [];
}

public class QuestionResult
{
    /// <summary>
    /// The question this result belongs to
    /// </summary>
    public string QuestionName { get; set; } = null!;

    /// <summary>
    /// The weight of the question
    /// </summary>
    public decimal Weight { get; set; }

    /// <summary>
    /// The percentage of the weight of the answer in relation to all other questions in the form
    /// </summary>
    public decimal Percentage { get; set; }

    /// <summary>
    /// The given answer
    /// </summary>
    public double Answer { get; set; }
}