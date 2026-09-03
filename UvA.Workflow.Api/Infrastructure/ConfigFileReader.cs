using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Infrastructure;

/// Flattens a content provider into path -> content, so a config version can be handed to an editor
/// and posted back to POST /Versions/{version}.
public static class ConfigFileReader
{
    public static Dictionary<string, string> ReadAll(IContentProvider provider)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var folders = new Queue<string?>();
        folders.Enqueue(null);

        while (folders.Count > 0)
        {
            var folder = folders.Dequeue();
            foreach (var child in provider.GetFolders(folder))
                folders.Enqueue(child);

            // The root itself holds no yaml the parser reads (it starts at Common and at folders with an
            // Entity.yaml), and neither provider can list files for a null directory.
            if (folder is null)
                continue;

            foreach (var file in provider.GetFiles(folder))
                files[file] = provider.GetFile(file);
        }

        return files;
    }
}