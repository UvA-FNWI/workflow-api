using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.WorkflowDefinitions.Dtos;

public record MessagesSettingsDto(
    bool Card = true,
    bool FieldsAllowAll = true,
    string Layout = "threaded",
    bool AllowClose = true,
    CalloutDto? Callout = null,
    BilingualString? CardTitle = null
)
{
    public static MessagesSettingsDto From(MessagesSettings settings) =>
        new(
            settings.Card,
            settings.Fields,
            string.IsNullOrWhiteSpace(settings.Layout) ? "threaded" : settings.Layout,
            settings.Close,
            settings.Callout is { } callout ? new CalloutDto(callout.Variant, callout.Title, callout.Text) : null,
            settings.CardTitle
        );
}