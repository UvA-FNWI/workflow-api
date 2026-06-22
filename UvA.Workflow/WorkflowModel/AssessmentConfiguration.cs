namespace UvA.Workflow.WorkflowModel;

/// <summary>
/// Configuration for the assessment of the entity
/// </summary>
public class AssessmentConfiguration
{
    /// <summary>
    /// The parts that together make up the assessment
    /// </summary>
    public List<AssessmentPart> Parts { get; set; } = [];
}

public class AssessmentPart : INamed
{
    /// <summary>
    /// Internal name of the assessment part
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
    /// The assessor sources that contribute to this part
    /// </summary>
    public List<AssessmentSource> Sources { get; set; } = [];
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
    /// Relative weight of this source within the assessment part. Defaults to 1.
    /// </summary>
    public decimal Weight { get; set; } = 1;
}