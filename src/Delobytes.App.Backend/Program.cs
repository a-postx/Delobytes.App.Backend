using Delobytes.App.Backend.Infrastructure;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Swashbuckle.AspNetCore.Swagger;
using System.Reflection;
using System.Text.Json;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Delobytes App Backend API",
        Version = "v1",
        Description = "Delobytes e-commerce margin accounting system API",
    });
});

// MediatR – scan all module application assemblies
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);

    // Add validation pipeline behavior
    cfg.AddOpenBehavior(typeof(Delobytes.App.Backend.Application.Behaviours.ValidationBehaviour<,>));
});

// FluentValidation
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// EF Core / PostgreSQL via module infrastructure registrations
builder.Services.AddInfrastructure(builder.Configuration);

// Health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty,
        name: "postgresql",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "db", "sql", "postgresql" });

WebApplication app = builder.Build();

// Apply EF Core migrations on startup
await app.Services.ApplyMigrationsAsync();

// Swagger
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Delobytes App Backend v1");
    options.RoutePrefix = string.Empty; // swagger at root
});

// Status endpoint – returns 200 OK with service info (non-Prometheus, human-readable)
app.MapGet("/status", () =>
{
    return Results.Ok(new
    {
        status = "ok",
        version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
        timestamp = DateTimeOffset.UtcNow,
    });
})
.WithName("Status")
.WithTags("System")
.Produces<object>(StatusCodes.Status200OK);

// Metrics / health endpoint
app.MapHealthChecks("/metrics", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
        string result = JsonSerializer.Serialize(
            new
            {
                status = report.Status.ToString(),
                totalDuration = report.TotalDuration,
                entries = report.Entries.ToDictionary(
                    e => e.Key,
                    e => new
                    {
                        status = e.Value.Status.ToString(),
                        duration = e.Value.Duration,
                        description = e.Value.Description,
                    }),
            },
            jsonOptions);
        await context.Response.WriteAsync(result);
    },
});

app.MapControllers();

await app.RunAsync();

// Needed for WebApplicationFactory in integration tests
public partial class Program { }
