using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Notifications;

public record MailButton(string Label, string Url, MailButtonIntent Intent = MailButtonIntent.Primary);

public interface IMailLayout
{
    string Render(string htmlBody, IReadOnlyList<MailButton> buttons);
}

public interface INamedMailLayout : IMailLayout
{
    string Key { get; }
}

public interface IMailLayoutResolver
{
    IMailLayout Resolve(string? key);
}

public class MailLayoutResolver(IEnumerable<INamedMailLayout> layouts) : IMailLayoutResolver
{
    public const string DefaultKey = "default";

    private readonly IReadOnlyDictionary<string, INamedMailLayout> _layouts = layouts
        .GroupBy(l => l.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Single(), StringComparer.OrdinalIgnoreCase);

    public IMailLayout Resolve(string? key)
    {
        key = string.IsNullOrWhiteSpace(key) ? DefaultKey : key.Trim();

        if (_layouts.TryGetValue(key, out var layout))
            return layout;

        var known = string.Join(", ", _layouts.Keys.OrderBy(k => k));
        throw new InvalidOperationException($"Unknown mail layout '{key}'. Known layouts: {known}");
    }
}

public abstract class FileMailLayout : INamedMailLayout
{
    private readonly string _layoutPath;
    private string? _cachedTemplate;
    private readonly object _cacheLock = new();

    protected FileMailLayout(string key, string layoutPath)
    {
        Key = key;
        _layoutPath = layoutPath;
    }

    public string Key { get; }

    public string Render(string htmlBody, IReadOnlyList<MailButton> buttons)
    {
        var template = GetCachedTemplate();
        var buttonHtml = GenerateButtonHtml(buttons);

        return template
            .Replace("{{htmlBody}}", htmlBody)
            .Replace("{{buttonHtml}}", buttonHtml);
    }

    private string GetCachedTemplate()
    {
        if (_cachedTemplate != null)
            return _cachedTemplate;

        lock (_cacheLock)
        {
            if (_cachedTemplate != null)
                return _cachedTemplate;

            if (!File.Exists(_layoutPath))
                throw new FileNotFoundException($"Layout file not found: {_layoutPath}");

            _cachedTemplate = File.ReadAllText(_layoutPath);
            return _cachedTemplate;
        }
    }

    protected virtual string GenerateButtonHtml(IReadOnlyList<MailButton> buttons)
    {
        if (buttons.Count == 0)
            return "";

        return string.Join("\n", buttons.Select(button =>
        {
            var intentClass = button.Intent switch
            {
                MailButtonIntent.Primary => "button-primary",
                _ => "button-primary"
            };

            var classAttr = $"button {intentClass}";

            return $"""
                    <tr>
                      <td align="center" style="padding: 0 0 24px 0;">
                        <a href="{button.Url}" class="{classAttr}">
                          {button.Label}
                        </a>
                      </td>
                    </tr>
                    """;
        }));
    }
}

public class DefaultMailLayout : FileMailLayout
{
    public DefaultMailLayout() : base(MailLayoutResolver.DefaultKey,
        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Examples",
            "Layouts", "default.html")))
    {
    }
}