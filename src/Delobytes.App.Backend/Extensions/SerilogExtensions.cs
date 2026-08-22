using Delobytes.App.Backend.Options;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;

namespace Delobytes.App.Backend.Extensions;

/// <summary>
/// Extension methods for configuring Serilog structured logging.
/// </summary>
internal static class SerilogExtensions
{
    /// <summary>
    /// Configures Serilog with Grafana Cloud Loki as the primary (and in production, exclusive) sink.
    /// In Development environment, Console sink is added for local diagnostics.
    /// File sink is intentionally NOT used per tech stack requirements (section 5.1).
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="secrets">Application secrets containing Loki credentials.</param>
    /// <returns>The same builder for chaining.</returns>
    public static WebApplicationBuilder AddSerilog(this WebApplicationBuilder builder, AppSecrets? secrets)
    {
        builder.Host.UseSerilog((context, services, loggerConfig) =>
        {
            loggerConfig
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithProcessId()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Application", "Delobytes.App.Backend")
                .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName);

            // Console sink: available in Development for local diagnostics only.
            // Disabled in Production to avoid leaking structured logs to stdout.
            if (context.HostingEnvironment.IsDevelopment())
            {
                loggerConfig.WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Debug);
            }

            // Grafana Cloud Loki log sink.
            if (!string.IsNullOrWhiteSpace(secrets?.LokiUrl))
            {
                LokiCredentials? credentials = null;

                if (!string.IsNullOrWhiteSpace(secrets.LokiUser) && !string.IsNullOrWhiteSpace(secrets.LokiPassword))
                {
                    credentials = new LokiCredentials
                    {
                        Login = secrets.LokiUser,
                        Password = secrets.LokiPassword,
                    };
                }

                LokiLabel[] labels = new[]
                        {
                            new LokiLabel { Key = "app", Value = "delobytes-backend" },
                            new LokiLabel { Key = "env", Value = context.HostingEnvironment.EnvironmentName },
                        };

                loggerConfig.WriteTo.GrafanaLoki(
                    uri: secrets.LokiUrl,
                    credentials: credentials,
                    labels: labels,
                    restrictedToMinimumLevel: LogEventLevel.Information);
            }
        });

        return builder;
    }
}
