using Delobytes.App.Backend.BackgroundServices;
using Delobytes.App.Backend.Contracts.Secrets;
using Delobytes.App.Backend.Extensions;
using Delobytes.App.Backend.Infrastructure;

namespace Delobytes.App.Backend.Composition;

internal static class InfrastructureComposition
{
    public static WebApplicationBuilder AddAppInfrastructure(this WebApplicationBuilder builder)
    {
        AppSecrets secrets = builder.Configuration.GetRequiredAppSecrets();

        builder.Services.AddInfrastructure(builder.Configuration, secrets);

        // Null connection string selects the in-memory transport, which is what tests and local
        // development use. Production is guarded by AppSecretsValidator: it refuses to start
        // without a broker unless Messaging:UseInMemory is set explicitly.
        bool useInMemoryTransport = builder.Configuration.GetValue("Messaging:UseInMemory", false)
            || string.IsNullOrWhiteSpace(secrets.MessageBusConnectionString);

        builder.Services.AddMessaging(useInMemoryTransport ? null : secrets.MessageBusConnectionString);

        // Separate IHostedServices: the scheduler only creates Pending JobRun rows per cron
        // schedule, the worker is the sole place that enforces per-tenant parallelism and
        // publishes the event that actually triggers the orchestration consumer.
        if (builder.Configuration.GetValue("Background:Enabled", true))
        {
            builder.Services.AddHostedService<SchedulerHostedService>();
            builder.Services.AddHostedService<WorkerHostedService>();
            builder.Services.AddHostedService<WatchdogHostedService>();
            builder.Services.AddHostedService<RawApiResponseCleanupHostedService>();
            builder.Services.AddHostedService<ExpectationAgingHostedService>();
        }

        return builder;
    }
}
