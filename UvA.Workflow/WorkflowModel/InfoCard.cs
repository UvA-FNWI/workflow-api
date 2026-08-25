using UvA.Workflow.Expressions;

namespace UvA.Workflow.WorkflowModel;

public class InfoCard : INamed
{
    public string Name { get; set; } = null!;
    public InfoCardType? Type { get; set; }
    public BilingualString? Title { get; set; }
    public bool Enabled { get; set; } = true;
    public string[]? Sources { get; set; }

    public string? User { get; set; }
    public InfoCardField[] Fields { get; set; } = [];
    public BilingualString? EmptyText { get; set; }

    public RelatedUserGroup[] Groups { get; set; } = [];
    public InfoCardItem[] Items { get; set; } = [];
    public BilingualString? Content { get; set; }

    [YamlIgnore]
    public IEnumerable<Lookup> Properties =>
    [
        ..(User == null ? Enumerable.Empty<Lookup>() : [(Lookup)new PropertyLookup(User)]),
        ..Fields.SelectMany(configuredField => configuredField.Properties),
        ..Groups.SelectMany(group => group.Users).Select(user => (Lookup)new PropertyLookup(user.Property)),
        ..Items.SelectMany(item => item.UrlTemplate?.Properties ?? [])
    ];
}

public enum InfoCardType
{
    User,
    RelatedUsers,
    Links,
    Text
}

public class InfoCardField : Field
{
    public string? Icon { get; set; }
}

public class InfoCardItem : INamed
{
    public string Name { get; set; } = null!;
    public InfoCardItemType Type { get; set; }
    public BilingualString Text { get; set; } = null!;
    public BilingualString? Url { get; set; }

    [YamlIgnore] public BilingualTemplate? UrlTemplate => field ??= BilingualTemplate.Create(Url);
}

public enum InfoCardItemType
{
    Link,
    Download
}