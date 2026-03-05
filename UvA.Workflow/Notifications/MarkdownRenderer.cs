using Markdig;

namespace UvA.Workflow.Notifications;

internal static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static string ToHtml(string markdown) => Markdown.ToHtml(markdown, Pipeline);
}