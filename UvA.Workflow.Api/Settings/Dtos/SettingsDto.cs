using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Settings.Dtos;

public record SettingsDto(
    InstitutionSettingsDto? Institution,
    UiSettingsDto? Ui)
{
    public static SettingsDto? Create(WorkflowSettings? settings) =>
        settings == null
            ? null
            : new SettingsDto(
                settings.Institution == null
                    ? null
                    : new InstitutionSettingsDto(settings.Institution.Name, settings.Institution.LogoMapping),
                settings.Ui == null ? null : UiSettingsDto.Create(settings.Ui));
}

public record InstitutionSettingsDto(
    string? Name,
    Dictionary<string, string>? LogoMapping);

public record UiSettingsDto(
    BilingualString? AppName,
    OverviewUiSettingsDto? Overview,
    PersonalPageUiSettingsDto? PersonalPage)
{
    public static UiSettingsDto Create(UiSettings ui) =>
        new(
            ui.AppName,
            ui.Overview == null ? null : OverviewUiSettingsDto.Create(ui.Overview),
            ui.PersonalPage == null ? null : PersonalPageUiSettingsDto.Create(ui.PersonalPage));
}

public record OverviewUiSettingsDto(
    BilingualString? PageTitle,
    BilingualString? Title,
    BilingualString? Subtitle,
    BilingualString? NoAccess)
{
    public static OverviewUiSettingsDto Create(OverviewUiSettings overview) =>
        new(overview.PageTitle, overview.Title, overview.Subtitle, overview.NoAccess);
}

public record PersonalPageUiSettingsDto(
    BilingualString? Title,
    BilingualString? PrimaryColumn)
{
    public static PersonalPageUiSettingsDto Create(PersonalPageUiSettings personalPage) =>
        new(personalPage.Title, personalPage.PrimaryColumn);
}