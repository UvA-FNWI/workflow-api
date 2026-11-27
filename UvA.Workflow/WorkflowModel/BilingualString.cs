using System.Diagnostics.CodeAnalysis;

namespace UvA.Workflow.WorkflowModel;

public class BilingualString
{
    public string En { get; set; } = "";
    public string Nl { get; set; } = "";

    public BilingualString()
    {
    }

    public BilingualString(string en, string nl)
    {
        En = en;
        Nl = nl;
    }

    public static bool IsDutch(string? language)
        => language?.StartsWith("nl", StringComparison.OrdinalIgnoreCase) == true;

    public string ForLanguage(string? language)
        => IsDutch(language) && !string.IsNullOrWhiteSpace(Nl) ? Nl : En;

    [return: NotNullIfNotNull(nameof(text))]
    public static BilingualString? Resolve(BilingualString? text, IReadOnlyList<string> languages)
    {
        if (text == null)
            return null;

        var resolved = text.WithFallback();
        return languages.Count == 1 ? (BilingualString)resolved.ForLanguage(languages[0]) : resolved;
    }

    internal BilingualString WithFallback()
    {
        var en = string.IsNullOrWhiteSpace(En) ? null : En;
        var nl = string.IsNullOrWhiteSpace(Nl) ? null : Nl;
        return new BilingualString(en ?? nl ?? "", nl ?? en ?? "");
    }

    public static implicit operator BilingualString(string text) => new() { En = text, Nl = text };

    public static BilingualString operator +(BilingualString s1, BilingualString? s2)
        => s2 == null ? s1 : new BilingualString(s1.En + s2.En, s1.Nl + s2.Nl);
}