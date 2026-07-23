namespace UvA.Workflow.WorkflowModel;

public class Resource : INamed
{
    /// <summary>
    /// Short internal name of the resource
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Localized title of the resource
    /// </summary>
    public BilingualString? Title { get; set; }

    /// <summary>
    /// The type of this resource, e.g. text or links
    /// </summary>
    public ResourceLayout Type { get; set; }

    /// <summary>
    /// List of items in this resource
    /// </summary>
    public List<ResourceItem> Items { get; set; } = null!;

    /// <summary>
    /// If set, this resource is included only for the matching sources
    /// </summary>
    public string[]? Sources { get; set; }
}

public class ResourceItem : INamed
{
    /// <summary>
    /// Short internal name of the resource item
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// The type of the resource, e.g. link, text or download
    /// </summary>
    public ResourceType Type { get; set; }

    /// <summary>
    /// Localized text of the resource item
    /// </summary>
    public BilingualString Text { get; set; } = null!;

    /// <summary>
    /// Optional (Localized) URL of the resource item, e.g. for a link or download
    /// </summary>
    public BilingualString? Url { get; set; }
}

public enum ResourceLayout
{
    Links,
    Text
}

public enum ResourceType
{
    Link,
    Download,
    Text
}