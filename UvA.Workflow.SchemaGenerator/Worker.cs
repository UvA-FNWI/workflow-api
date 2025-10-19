using NJsonSchema;
using NJsonSchema.Generation.TypeMappers;
using UvA.Workflow.Entities.Domain;
using UvA.Workflow.SchemaGenerator.Generation;

namespace UvA.Workflow.SchemaGenerator;

public class Worker(ILogger<Worker> logger, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = new DocumentationReader();
        await reader.Load(stoppingToken);
        var generator = new Generator(reader);

        foreach (var type in new[] {typeof(EntityType), typeof(Form), typeof(Screen), typeof(Role), typeof(Step)})
        {
            var schema = generator.Generate(type).ToJson();
            await File.WriteAllTextAsync($"../Schemas/{type.Name}.json", schema, stoppingToken);
        }

        lifetime.StopApplication();
    }
}

class TestTypeMapper : ITypeMapper
{
    public void GenerateSchema(JsonSchema schema, TypeMapperContext context)
    {
        throw new NotImplementedException();
    }

    public Type MappedType { get; }
    public bool UseReference { get; }
}