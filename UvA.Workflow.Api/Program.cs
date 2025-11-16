using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowInstances.Dtos;

string corsPolicyName = "_CorsPolicy";

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
builder.Services.AddScoped<WorkflowInstanceDtoFactory>();
builder.Services
    .AddControllers()
    .AddJsonOptions(opts => { opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()); });
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddSurfConextAuthentication(builder.Environment, builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicyName,
        cb =>
        {
            cb.SetIsOriginAllowedToAllowWildcardSubdomains()
                .WithOrigins(config["AllowedOrigins"]!.Split(','))
                .AllowAnyMethod()
                .AllowCredentials()
                .AllowAnyHeader()
                .WithExposedHeaders("Content-Disposition");
        });
});

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

app.UseCors(corsPolicyName);

app.Services.GetRequiredService<ModelServiceResolver>().AddOrUpdate("", new ModelParser(
    new FileSystemProvider(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../Examples/Projects"))
));

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    if (app.Environment.IsDevOrTest())
    {
        c.OAuthClientId(app.Environment.IsDevelopment() ? "datanose.local" : "v2-tst.datanose.nl");
        c.OAuthUsePkce();
    }

    c.SwaggerEndpoint("v1/swagger.json", "Workflow API v1");
    c.DisplayRequestDuration();
});

app.MapControllers();
app.Run();