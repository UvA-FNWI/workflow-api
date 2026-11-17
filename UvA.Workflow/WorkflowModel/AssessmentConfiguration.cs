using System.ComponentModel;

namespace UvA.Workflow.WorkflowModel;

/// <summary>
/// Possible types of the grading
/// </summary>
public enum GradingBasis
{
    Half,
    Decimal,
    PassFail
}

/// <summary>
/// Configuration for the assessment of the entity
/// </summary>
public class AssessmentConfiguration
{
    /// <summary>
    /// The parts that together make up the assessment
    /// </summary>
    public List<AssessmentPart> Parts { get; set; } = [];

    /// <summary>
    /// Property to indicate the final grade type
    /// </summary>
    public GradingBasis? GradingBasis { get; set; }

    /// <summary>
    /// Determines whether grades between 5 and 6 are allowed
    /// </summary>
    public bool? GradeGap { get; set; }

    public AssessmentConfiguration Enrich(string? gradingBasis, bool? gradeGap)
    {
        var contextGradingBasis = gradingBasis is string s
                                  && Enum.TryParse<GradingBasis>(s, out var parsed)
            ? parsed
            : (GradingBasis?)null;

        var enrichedConfig = new AssessmentConfiguration
        {
            GradingBasis = GradingBasis ?? contextGradingBasis ?? WorkflowModel.GradingBasis.Decimal,
            GradeGap = GradeGap ?? gradeGap ?? false,
            Parts = Parts,
        };

        return enrichedConfig;
    }
}

public class AssessmentPart : INamed
{
    /// <summary>
    /// Internal name of the assessment part. Use an imported step's local name to resolve its source assessment.
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Localized title of the assessment part
    /// </summary>
    public BilingualString? Title { get; set; }

    /// <summary>
    /// Weight of this part in the final grade calculation
    /// </summary>
    public decimal Weight { get; set; }

    /// <summary>
    /// Show a warning if the discrepancy between the scores is larger than or equal to this value
    /// </summary>
    public decimal MaximumDiscrepancy { get; set; }

    /// <summary>
    /// Warnings to be shown if there are discrepancies in the grades
    /// </summary>
    public AssessmentWarning[] Warnings { get; set; } = [];

    /// <summary>
    /// The assessor sources that contribute to this part
    /// </summary>
    public List<AssessmentSource> Sources { get; set; } = [];
}

public class AssessmentWarning
{
    /// <summary>
    /// Show a warning if the discrepancy between the scores is larger than or equal to this value
    /// </summary>
    [DefaultValue(0)]
    public decimal MaximumDiscrepancy { get; set; }

    /// <summary>
    /// Show a warning if one of the grades is above this and another below
    /// </summary>
    [DefaultValue(0)]
    public decimal InflectionPoint { get; set; }

    public BilingualString Label { get; set; } = null!;
}

/// <summary>
/// An assessor source within an assessment part, referencing a property by name
/// </summary>
public class AssessmentSource : INamed
{
    /// <summary>
    /// Name of the property (assessor) that provides the score for this part
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Localized title of the source
    /// </summary>
    public BilingualString? Title { get; set; }

    /// <summary>
    /// Relative weight of this source within the assessment part. Defaults to 1.
    /// </summary>
    public decimal Weight { get; set; } = 1;
}