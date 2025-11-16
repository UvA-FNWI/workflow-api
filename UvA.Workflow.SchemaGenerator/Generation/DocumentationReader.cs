using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace UvA.Workflow.SchemaGenerator.Generation;

public class DocumentationReader
{
    private DocumentationModel? _documentation;

    public async Task Load(CancellationToken cancellationToken)
    {
        XmlSerializer serializer = new XmlSerializer(typeof(DocumentationModel));
        var docFilePath = Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!,
            "UvA.Workflow.xml");
        var docText = await File.ReadAllTextAsync(docFilePath, cancellationToken);
        docText = ConvertInlineXmlDocTagsToHtml(docText);

        using var reader = new StringReader(docText);
        _documentation = (DocumentationModel?)serializer.Deserialize(reader);
    }

    private static string ConvertInlineXmlDocTagsToHtml(string input)
    {
        // <c>code</c> -> HTML encoded <code>code</code>
        input = Regex.Replace(input,
            @"<c>(.*?)</c>",
            match => WebUtility.HtmlEncode($"<code>{match.Groups[1].Value}</code>"),
            RegexOptions.IgnoreCase);

        // <code>block</code> -> HTML encoded <pre><code>block</code></pre>
        input = Regex.Replace(input,
            @"<code>(.*?)</code>",
            match => WebUtility.HtmlEncode($"<pre><code>{match.Groups[1].Value}</code></pre>"),
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // <para>text</para> -> HTML encoded <p>text</p>
        input = Regex.Replace(input,
            @"<para>(.*?)</para>",
            match => WebUtility.HtmlEncode($"<p>{match.Groups[1].Value}</p>"),
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // <see cref=".." /> -> HTML encoded <p>text</p>
        input = Regex.Replace(input,
            @"<(see|paramref) (.*?)/>",
            match => WebUtility.HtmlEncode(match.Value),
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return input;
    }

    public string? GetSummary(PropertyInfo property)
    {
        if (_documentation == null)
            throw new InvalidOperationException("Documentation not loaded");

        return _documentation.Members.Member
            .FirstOrDefault(x => x.Name == $"P:{property.DeclaringType?.FullName}.{property.Name}")?
            .Summary.Trim();
    }

    public string? GetSummary(Type type)
    {
        if (_documentation == null)
            throw new InvalidOperationException("Documentation not loaded");

        return _documentation.Members.Member
            .FirstOrDefault(x => x.Name == $"T:{type.FullName}")?
            .Summary.Trim();
    }
}

[XmlRoot(ElementName = "assembly")]
public class Assembly
{
    [XmlElement(ElementName = "name")] public string Name { get; set; } = null!;
}

[XmlRoot(ElementName = "member")]
public class Member
{
    [XmlElement(ElementName = "summary")] public string Summary { get; set; } = null!;

    [XmlAttribute(AttributeName = "name")] public string Name { get; set; } = null!;

    [XmlText] public string Text { get; set; } = null!;

    [XmlElement(ElementName = "returns")] public object Returns { get; set; } = null!;

    [XmlElement(ElementName = "param")] public List<Param> Parameters { get; set; } = null!;
}

[XmlRoot(ElementName = "param")]
public class Param
{
    [XmlAttribute(AttributeName = "name")] public string Name { get; set; } = null!;

    [XmlText] public string Text { get; set; } = null!;

    [XmlElement(ElementName = "c")] public bool C { get; set; }
}

[XmlRoot(ElementName = "members")]
public class Members
{
    [XmlElement(ElementName = "member")] public List<Member> Member { get; set; } = null!;
}

[XmlRoot(ElementName = "doc")]
public class DocumentationModel
{
    [XmlElement(ElementName = "assembly")] public Assembly Assembly { get; set; } = null!;

    [XmlElement(ElementName = "members")] public Members Members { get; set; } = null!;
}