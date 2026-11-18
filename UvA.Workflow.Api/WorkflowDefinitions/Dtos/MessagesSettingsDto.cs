using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.WorkflowDefinitions.Dtos;

public record MessagesSettingsDto(
    bool Card = true,
    bool FieldsAllowAll = true,
    string Layout = "threaded",
    bool AllowClose = true,
    bool Callout = false
)
{
    public static MessagesSettingsDto From(MessagesSettings settings) =>
        new(
            settings.Card,
            settings.Fields,
            string.IsNullOrWhiteSpace(settings.Layout) ? "threaded" : settings.Layout,
            settings.Close,
            settings.Callout
        );
}