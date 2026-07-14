using UvA.Workflow.WorkflowModel;

var projectsDir = args.Length > 0 ? args[0] : "Projects";
var fullPath = Path.GetFullPath(projectsDir);
Console.WriteLine($"Validating workflow config at: {fullPath}");

if (!Directory.Exists(fullPath))
{
    Console.Error.WriteLine($"Projects directory not found: {fullPath}");
    return 1;
}

try
{
    _ = new ModelParser(new FileSystemProvider(fullPath));
    Console.WriteLine("Config is valid.");
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"Config validation FAILED: {e.Message}");
    return 1;
}