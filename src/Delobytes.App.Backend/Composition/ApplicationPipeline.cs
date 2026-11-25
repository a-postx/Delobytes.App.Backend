using System.Reflection;
using System.Text.Json;
using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Messaging.Events;
using Delobytes.App.Backend.Middleware;
using Delobytes.AspNetCore.Common.Constants;
using Delobytes.AspNetCore.Logging;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Delobytes.App.Backend.Composition;

internal static class ApplicationPipeline
{
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();

        app.UseHttpContextLogging();

        app.UseMiddleware<ExceptionHandlingMiddleware>();

        app.UseCors(CorsPolicyNames.AllowAny);

        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Delobytes App Backend v1");
            options.RoutePrefix = "swagger";
        });

        app.UseRateLimiter();

        // Authentication & authorization middleware must come before MapControllers.
        app.UseAuthentication()
            .UseClaimsLogging();
        app.UseAuthorization();

        return app;
    }

    public static WebApplication MapSystemEndpoints(this WebApplication app)
    {
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

        return app;
    }

    /// <summary>
    /// Publishes a startup event so that a misconfigured broker becomes visible in logs immediately.
    /// </summary>
    public static async Task PublishStartedEventAsync(this WebApplication app)
    {
        string appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

        using IServiceScope publishScope = app.Services.CreateScope();
        IPublishEndpoint publishEndpoint = publishScope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await publishEndpoint.Publish(new AppStartedEvent
        {
            StartedAt = DateTimeOffset.UtcNow,
            ApplicationVersion = appVersion,
        });
    }
}
