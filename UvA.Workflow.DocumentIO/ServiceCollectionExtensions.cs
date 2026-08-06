using Microsoft.Extensions.DependencyInjection;
using UvA.Workflow.DocumentIO;

namespace UvA.Workflow.DocumentIO;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowDocumentIO(this IServiceCollection services)
    {
        services.AddScoped<IExcelService, ExcelService>();
        return services;
    }
}