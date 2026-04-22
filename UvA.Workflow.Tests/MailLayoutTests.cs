using Moq;
using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Notifications;

namespace UvA.Workflow.Tests;

public class MailLayoutResolverTests
{
    private static INamedMailLayout LayoutWithKey(string key)
    {
        var mock = new Mock<INamedMailLayout>();
        mock.Setup(l => l.Key).Returns(key);
        return mock.Object;
    }

    [Fact]
    public void Resolve_WithExactKey_ReturnsMatchingLayout()
    {
        var custom = LayoutWithKey("custom");
        var resolver = new MailLayoutResolver([LayoutWithKey("default"), custom]);

        Assert.Same(custom, resolver.Resolve("custom"));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("Default")]
    [InlineData("DEFAULT")]
    public void Resolve_IsCaseInsensitive(string key)
    {
        var layout = LayoutWithKey("default");
        var resolver = new MailLayoutResolver([layout]);

        Assert.Same(layout, resolver.Resolve(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_WithNullOrWhitespaceKey_FallsBackToDefault(string? key)
    {
        var layout = LayoutWithKey("default");
        var resolver = new MailLayoutResolver([layout]);

        Assert.Same(layout, resolver.Resolve(key));
    }

    [Fact]
    public void Resolve_WithUnknownKey_ThrowsWithKeyAndKnownLayoutsInMessage()
    {
        var resolver = new MailLayoutResolver([LayoutWithKey("default")]);

        var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve("nonexistent"));
        Assert.Contains("nonexistent", ex.Message);
        Assert.Contains("default", ex.Message);
    }
}

public class FileMailLayoutTests
{
    private class TestFileMailLayout(string key, string layoutPath) : FileMailLayout(key, layoutPath);

    private static (TestFileMailLayout layout, string tempFile) CreateLayout(string template)
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, template);
        return (new TestFileMailLayout("test", tempFile), tempFile);
    }

    [Fact]
    public void Render_InjectsHtmlBody()
    {
        var (layout, temp) = CreateLayout("BEFORE {{htmlBody}} AFTER {{buttonHtml}}");
        try
        {
            var result = layout.Render("<p>content</p>", []);
            Assert.Contains("<p>content</p>", result);
            Assert.Contains("BEFORE", result);
            Assert.Contains("AFTER", result);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void Render_WithNoButtons_ReplacesButtonHtmlWithEmpty()
    {
        var (layout, temp) = CreateLayout("{{htmlBody}}[{{buttonHtml}}]");
        try
        {
            var result = layout.Render("body", []);
            Assert.Equal("body[]", result);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void Render_WithSingleButton_RendersLinkWithLabelAndUrl()
    {
        var (layout, temp) = CreateLayout("{{htmlBody}}{{buttonHtml}}");
        try
        {
            var button = new MailButton("Open Form", "https://example.com/form", MailButtonIntent.Primary);
            var result = layout.Render("", [button]);

            Assert.Contains("href=\"https://example.com/form\"", result);
            Assert.Contains("Open Form", result);
            Assert.Contains("padding:12px 28px;", result);
            Assert.Contains("background-color:#E00031;color:#FFFFFF;", result);
            Assert.Contains("display:inline-block;", result);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void Render_WithMultipleButtons_RendersAllInOrder()
    {
        var (layout, temp) = CreateLayout("{{htmlBody}}{{buttonHtml}}");
        try
        {
            var buttons = new[]
            {
                new MailButton("First", "https://example.com/1", MailButtonIntent.Primary),
                new MailButton("Second", "https://example.com/2", MailButtonIntent.Primary),
            };
            var result = layout.Render("", buttons);

            var firstIndex = result.IndexOf("First", StringComparison.Ordinal);
            var secondIndex = result.IndexOf("Second", StringComparison.Ordinal);
            Assert.True(firstIndex < secondIndex, "First button should appear before second button");
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void Render_CachesTemplate_FileIsOnlyReadOnce()
    {
        var (layout, temp) = CreateLayout("{{htmlBody}}{{buttonHtml}}");

        layout.Render("first", []);
        File.Delete(temp); // delete so a second file read would throw

        // Should not throw — template must be served from cache
        var result = layout.Render("cached", []);
        Assert.Contains("cached", result);
    }

    [Fact]
    public void Render_WhenFileNotFound_ThrowsFileNotFoundException()
    {
        var layout = new TestFileMailLayout("test", "/nonexistent/path/layout.html");

        Assert.Throws<FileNotFoundException>(() => layout.Render("body", []));
    }
}

public class DefaultMailLayoutIntegrationTests
{
    [Fact]
    public void DefaultLayout_FileExistsAndLoadsWithoutError()
    {
        var layout = new DefaultMailLayout();

        // Should not throw
        var result = layout.Render("<p>Hello</p>", []);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void DefaultLayout_RenderedOutput_ContainsNoUnresolvedPlaceholders()
    {
        var layout = new DefaultMailLayout();
        var button = new MailButton("Click here", "https://example.com", MailButtonIntent.Primary);

        var result = layout.Render("<p>Hello</p>", [button]);

        Assert.DoesNotContain("{{", result);
        Assert.DoesNotContain("}}", result);
    }

    [Fact]
    public void DefaultLayout_RenderedOutput_ContainsBodyContent()
    {
        var layout = new DefaultMailLayout();

        var result = layout.Render("<p>Test body</p>", []);

        Assert.Contains("<p>Test body</p>", result);
    }
}