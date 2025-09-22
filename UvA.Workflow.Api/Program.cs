using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("ApplicationName", "Workflow-Api")
        .WriteTo.Debug(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{user.id}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{user.id}] {Message:lj}{NewLine}{Exception}");
    //.WriteTo.ApplicationInsights(services.GetRequiredService<TelemetryConfiguration>(), TelemetryConverter.Traces);
});

builder.Services.AddControllers();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("swagger/openapi/documentation.json");
    app.UseSwaggerUI(c =>
    {
        // if (app.Environment.IsDevOrTest())
        // {
        //     c.OAuthClientId(app.Environment.IsDevelopment() ? "datanose.local" : "v2-tst.datanose.nl");
        //     c.OAuthUsePkce();
        // }

        //c.SwaggerEndpoint();
        c.DisplayRequestDuration();
    });
}

app.MapControllers();
app.Run();

