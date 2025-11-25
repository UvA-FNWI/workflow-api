using UvA.Workflow.Entities.Domain;
using UvA.Workflow.SchemaGenerator.Generation;

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

        foreach (var type in types)
        {
            var schema = generator.Generate(type).ToJson();
            await File.WriteAllTextAsync($"../Schemas/{type.Name}.json", schema, stoppingToken);
        }

        lifetime.StopApplication();
    }
}