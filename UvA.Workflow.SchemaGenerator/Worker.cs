using UvA.Workflow.SchemaGenerator.Generation;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.SchemaGenerator;

public class Worker(IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = new DocumentationReader();
        await reader.Load(stoppingToken);
        var generator = new Generator(reader);

        Type[] types =
        [
            typeof(WorkflowDefinition),
            typeof(Form),
            typeof(Screen),
            typeof(Role),
            typeof(Step),
            typeof(ValueSet)
        ];

        var schemasChanged = false;
        foreach (var type in types)
        {
            var schema = generator.Generate(type).ToJson();
            schemasChanged |= await WriteSchema($"../Schemas/{type.Name}.json", schema, stoppingToken);
        }

        Environment.ExitCode = schemasChanged ? 1 : 0;
        lifetime.StopApplication();
    }

    public static async Task<bool> WriteSchema(string path, string schema, CancellationToken cancellationToken)
    {
        if (File.Exists(path) && await File.ReadAllTextAsync(path, cancellationToken) == schema)
            return false;

        await File.WriteAllTextAsync(path, schema, cancellationToken);
        return true;
    }
}