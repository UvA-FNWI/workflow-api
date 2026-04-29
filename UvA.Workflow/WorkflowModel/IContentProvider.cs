namespace UvA.Workflow.Entities.Domain;

/// <summary>
/// Represents a way to get yaml files from a content source.
/// </summary>
public interface IContentProvider
{
    IEnumerable<string> GetFolders(string? directory = null);
    IEnumerable<string> GetFiles(string directory);
    string GetFile(string file);
}

/// <summary>
/// Get yaml files from a dictionary.
/// </summary>
/// <param name="content">Dictionary of file paths and file content</param>
public class DictionaryProvider(Dictionary<string, string> content) : IContentProvider
{
    private readonly Dictionary<string, string> _content = content.ToDictionary(
        entry => NormalizePath(entry.Key),
        entry => entry.Value,
        StringComparer.Ordinal);

    public IEnumerable<string> GetFolders(string? directory = null)
    {
        var normalizedDirectory = string.IsNullOrWhiteSpace(directory) ? null : NormalizePath(directory);
        var prefix = normalizedDirectory == null ? "" : normalizedDirectory + "/";

        return _content.Keys
            .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
            .Select(path => path[prefix.Length..])
            .Select(path => path.Split('/')[0])
            .Where(path => !path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Select(path => normalizedDirectory == null ? path : $"{normalizedDirectory}/{path}");
    }

    public IEnumerable<string> GetFiles(string directory)
    {
        var normalizedDirectory = NormalizePath(directory);
        return _content.Keys
            .Where(path => path.StartsWith(normalizedDirectory + "/", StringComparison.Ordinal) &&
                           path.IndexOf('/', normalizedDirectory.Length + 1) == -1);
    }

    public string GetFile(string file)
        => _content[NormalizePath(file)];

    private static string NormalizePath(string path)
        => path.Replace('\\', '/').Trim('/');
}

/// <summary>
/// Get yaml files from the file system.
/// </summary>
/// <param name="rootPath">Folder to load from</param>
public class FileSystemProvider(string rootPath) : IContentProvider
{
    private readonly string _rootPath = Path.GetFullPath(rootPath);

    public IEnumerable<string> GetFolders(string? directory = null)
    {
        var path = string.IsNullOrWhiteSpace(directory) ? _rootPath : Resolve(directory);
        return Directory.Exists(path)
            ? Directory.GetDirectories(path)
                .Where(d => !Path.GetFileName(d).StartsWith('.'))
                .Select(ToRelative)
            : [];
    }

    public IEnumerable<string> GetFiles(string folder)
        => Directory.Exists(Resolve(folder))
            ? Directory.GetFiles(Resolve(folder), "*.yaml")
                .Select(ToRelative)
            : [];

    public string GetFile(string file) => File.ReadAllText(Resolve(file));

    private string Resolve(string path)
        => Path.IsPathRooted(path) ? path : Path.Combine(_rootPath, path);

    private string ToRelative(string path)
        => Path.GetRelativePath(_rootPath, path).Replace('\\', '/');
}