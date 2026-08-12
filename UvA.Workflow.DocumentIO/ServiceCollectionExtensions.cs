using Microsoft.Extensions.DependencyInjection;
using UvA.Workflow.Import;

namespace UvA.Workflow.DocumentIO;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowDocumentIo(this IServiceCollection services)
    {
        services.AddScoped<IFileParserService, ExcelService>();
        services.AddScoped<IFileParserService, CsvService>();
        return services;
    }
}