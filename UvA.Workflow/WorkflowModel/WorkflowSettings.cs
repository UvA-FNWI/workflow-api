namespace UvA.Workflow.WorkflowModel;

/// <summary>
/// Deployment settings from repo-root Settings.yaml (institution branding and UI copy).
/// </summary>
public class WorkflowSettings
{
    public InstitutionSettings? Institution { get; set; }
    public UiSettings? Ui { get; set; }
}

public class InstitutionSettings
{
    public string? Name { get; set; }
    public Dictionary<string, string>? LogoMapping { get; set; }
}

public class UiSettings
{
    public BilingualString? AppName { get; set; }
    public OverviewUiSettings? Overview { get; set; }
    public PersonalPageUiSettings? PersonalPage { get; set; }
}

public class OverviewUiSettings
{
    public BilingualString? PageTitle { get; set; }
    public BilingualString? Title { get; set; }
    public BilingualString? Subtitle { get; set; }
    public BilingualString? NoAccess { get; set; }
}

public class PersonalPageUiSettings
{
    public BilingualString? Title { get; set; }
    public BilingualString? PrimaryColumn { get; set; }
}