namespace UvA.Workflow.Entities.Domain;

/// <summary>
/// Represents a way to get yaml files from a content source.
/// </summary>
public interface IContentProvider
{
    IEnumerable<string> GetFolders();
    IEnumerable<string> GetFiles(string directory);
    string GetFile(string file);
}

/// <summary>
/// Get yaml files from a dictionary.
/// </summary>
/// <param name="content">Dictionary of file paths and file content</param>
public class DictionaryProvider(Dictionary<string, string> content) : IContentProvider
{
    public IEnumerable<string> GetFolders()
        => content.Keys.Select(s => s.Split('/')[0]).Distinct();

    public IEnumerable<string> GetFiles(string directory)
        => content.Keys.Where(s => s.StartsWith(directory + "/") && s.IndexOf('/', directory.Length + 1) == -1);

    public string GetFile(string file)
        => content[file];
}

/// <summary>
/// Get yaml files from the file system.
/// </summary>
/// <param name="rootPath">Folder to load from</param>
public class FileSystemProvider(string rootPath) : IContentProvider
{
    public IEnumerable<string> GetFolders() 
        => Directory.GetDirectories(rootPath).Where(d => !Path.GetFileName(d).StartsWith('.'));

    public IEnumerable<string> GetFiles(string folder) 
        => Directory.Exists(Path.Combine(rootPath, folder)) ? Directory.GetFiles(Path.Combine(rootPath, folder), "*.yaml") : [];

    public string GetFile(string file) => File.ReadAllText(file);
}