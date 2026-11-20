using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.WorkflowDefinitions.Dtos;

public record MessagesSettingsDto(
    bool FieldsAllowAll = true,
    string Layout = "threaded",
    bool AllowClose = true,
    CalloutDto? Callout = null,
    BilingualString? Term = null,
    BilingualString? TermPlural = null
)
{
    public static MessagesSettingsDto From(MessagesSettings settings) =>
        new(
            settings.Fields,
            string.IsNullOrWhiteSpace(settings.Layout) ? "threaded" : settings.Layout,
            settings.Close,
            settings.Callout is { } callout ? new CalloutDto(callout.Variant, callout.Title, callout.Text) : null,
            settings.Term,
            settings.TermPlural
        );
}