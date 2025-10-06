using Serilog;
using UvA.Workflow.Api.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("ApplicationName", "Workflow-Api")
        .WriteTo.Debug(
            outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{user.id}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.Console(
            outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{user.id}] {Message:lj}{NewLine}{Exception}");
    //.WriteTo.ApplicationInsights(services.GetRequiredService<TelemetryConfiguration>(), TelemetryConverter.Traces);
});


var config = builder.Configuration;
config.AddJsonFile("appsettings.local.json", true, true);
builder.Services.AddWorkflow(config);
builder.Services.AddControllers();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(c =>
    {
        // if (app.Environment.IsDevOrTest())
        // {
        //     c.OAuthClientId(app.Environment.IsDevelopment() ? "datanose.local" : "v2-tst.datanose.nl");
        //     c.OAuthUsePkce();
        // }
        c.RoutePrefix = string.Empty;
        c.SwaggerEndpoint("/openapi/v1.json", "Workflow API v1");
        c.DisplayRequestDuration();
    });
}

app.MapControllers();
app.Run();