using System.Text.Json.Serialization;
using Serilog;
using UvA.Workflow;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Api.Authentication.CanvasLti;
using UvA.Workflow.Api.Authentication.SurfConext;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Notifications;
using UvA.Workflow.Notifications.Graph;
using UvA.Workflow.Persistence.Mongo;
using UvA.Workflow.Users.DataNose;
using UvA.Workflow.Users.EduId;
using UvA.Workflow.WorkflowModel;

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
builder.Services.AddWorkflowCore();
builder.Services.AddWorkflowApiCore();
builder.Services
    .AddControllers()
    .AddJsonOptions(opts => { opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()); });
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddWorkflowAuthenticationSelector(builder.Configuration);
builder.Services.AddWorkflowMongoPersistence(builder.Configuration);
builder.Services.AddWorkflowGraphMail(builder.Configuration);
builder.Services.AddWorkflowDataNoseUsers(builder.Configuration);
builder.Services.AddWorkflowEduIdUsers(builder.Configuration);
builder.Services.AddWorkflowSurfConextAuthentication(builder.Configuration);
builder.Services.AddWorkflowCanvasLtiAuthentication(builder.Environment, builder.Configuration);

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
app.UseWorkflowAuthenticationSelector();
app.UseWorkflowCanvasLti(app.Configuration);

app.Services.GetRequiredService<ModelServiceResolver>().AddOrUpdate("", new ModelParser(
    new FileSystemProvider(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../Examples/Projects"))
));

await app.Services.CreateScope().ServiceProvider.GetRequiredService<InitializationService>().CreateSeedData();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("v1/swagger.json", "Workflow API v1");
    c.DisplayRequestDuration();
});

app.MapControllers();
app.Run();