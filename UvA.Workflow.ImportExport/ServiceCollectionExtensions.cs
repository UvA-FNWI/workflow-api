using Microsoft.Extensions.DependencyInjection;
using UvA.Workflow.Import;

namespace UvA.Workflow.ImportExport;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowImportExport(this IServiceCollection services)
    {
        services.AddScoped<IFileParserService, ExcelService>();
        services.AddScoped<IFileParserService, CsvService>();
        return services;
    }
}