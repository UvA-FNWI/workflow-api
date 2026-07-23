using UvA.Workflow.WorkflowModel;

var configDir = args.Length > 0 ? args[0] : ".";
var fullPath = Path.GetFullPath(configDir);
Console.WriteLine($"Validating workflow config at: {fullPath}");

if (!Directory.Exists(fullPath))
{
    Console.Error.WriteLine($"Config directory not found: {fullPath}");
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