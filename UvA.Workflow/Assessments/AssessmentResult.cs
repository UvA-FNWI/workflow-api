using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Assessments;

public class AssessmentResult
{
    public List<AssessmentPartResult> PartResults { get; set; } = [];
    public decimal FinalGradeUnrounded { get; set; }
    public float FinalGradeRounded { get; set; }
    public AssessmentConfiguration AssessmentConfiguration { get; set; } = null!;
}

public class AssessmentPartResult : INamed
{
    public string Name { get; set; } = null!;

    public SourceResult Combined { get; set; } = null!;

    public decimal PartPercentage { get; set; }

    public List<SourceResult> SourceResults { get; set; } = [];

    public AssessmentPart PartConfig { get; set; } = null!;

    public IEnumerable<SourceResult> AllResults => [..SourceResults, Combined];
}

public class SourceResult : INamed
{
    public const string Combined = "Combined";

    public bool IsCombined => Name == Combined;
    public string Name { get; set; } = null!;
    public decimal WeightedAverage { get; set; }
    public List<PageResult> PageResults { get; set; } = [];
    public Form Form { get; set; } = null!;
}

public class PageResult : INamed
{
    public string Name { get; set; } = null!;
    public decimal? Weight { get; set; }
    public decimal? WeightedAverage { get; set; }
    public List<QuestionResult> QuestionResults { get; set; } = [];
    public decimal Sum { get; set; }
}

public class QuestionResult : INamed
{
    /// <summary>
    /// The question this result belongs to
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// The weight of the question
    /// </summary>
    public decimal? Weight { get; set; }

    /// <summary>
    /// The percentage of the weight of the answer in relation to all other questions in the form
    /// </summary>
    public decimal? Percentage { get; set; }

    /// <summary>
    /// The given answer
    /// </summary>
    public double Answer { get; set; }

    public CalculationType Type { get; set; }
}