using Markdig;
using Markdig.Extensions.ListExtras;

namespace UvA.Workflow.Notifications;

internal static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = CreatePipeline();

    private static MarkdownPipeline CreatePipeline()
    {
        var builder = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions();

        // The ListExtraExtension provides support for lists using an alpha letter instead of a number,
        // this does not match with display names starting with letters, e.g. 'A. Smith', so we remove it from the pipeline.
        builder.Extensions.TryRemove<ListExtraExtension>();

        return builder.Build();
    }

    public static string ToHtml(string markdown) => Markdown.ToHtml(markdown, Pipeline);
}