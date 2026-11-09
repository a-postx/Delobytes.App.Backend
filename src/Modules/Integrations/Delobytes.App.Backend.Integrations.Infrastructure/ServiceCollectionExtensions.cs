using System.Reflection;
using Delobytes.App.Backend.Integrations.Application.SalesChannels;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Application.Orders;
using Delobytes.App.Backend.Integrations.Application.Products;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.RateLimiting;
using Delobytes.App.Backend.Integrations.Infrastructure.SalesChannels;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Integrations.Infrastructure.Options.Validators;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;
using Delobytes.App.Backend.Integrations.Infrastructure.Policies;
using Delobytes.App.Backend.Integrations.Infrastructure.Services;
using MassTransit;
using MassTransit.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Wildberries;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.YandexKit;

namespace Delobytes.App.Backend.Integrations.Infrastructure;

/// <summary>
/// Registers Integrations module infrastructure services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Integrations infrastructure services to the DI container.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionString">Connection string.</param>
    /// <param name="configuration">Configuration.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddIntegrationsInfrastructure(
        this IServiceCollection services,
        string? connectionString,
        IConfiguration? configuration = null)
    {
        if (connectionString == null)
        {
            throw new InvalidOperationException("Connection string is not configured.");
        }

        services.AddOptions<WildberriesImportOptions>()
            .Bind(configuration?.GetSection("WildberriesImport") ?? new ConfigurationBuilder().Build().GetSection("WildberriesImport"))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<WildberriesImportOptions>, WildberriesImportOptionsValidator>();

        services.AddOptions<OzonImportOptions>()
            .Bind(configuration?.GetSection("OzonImport") ?? new ConfigurationBuilder().Build().GetSection("OzonImport"))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<OzonImportOptions>, OzonImportOptionsValidator>();

        services.AddOptions().Configure<WildberriesRateLimitOptions>(
            configuration?.GetSection("WildberriesRateLimit") ?? new ConfigurationBuilder().Build().GetSection("WildberriesRateLimit"));

        services.Configure<BackgroundJobsOptions>(
            configuration?.GetSection("BackgroundJobs") ?? new ConfigurationBuilder().Build().GetSection("BackgroundJobs"));

        services.Configure<WatchdogOptions>(
            configuration?.GetSection("Watchdog") ?? new ConfigurationBuilder().Build().GetSection("Watchdog"));

        services.Configure<RawApiResponseCleanupOptions>(
            configuration?.GetSection("RawApiResponseCleanup") ?? new ConfigurationBuilder().Build().GetSection("RawApiResponseCleanup"));

        services.AddDbContext<IntegrationsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.MigrationsHistoryTable("__IntegrationsMigrationsHistory", "integrations")));

        services.AddMemoryCache();

        services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        services.AddScoped<IConnectionRepository, ConnectionRepository>();
        services.AddScoped<IJobRunRepository, JobRunRepository>();
        services.AddScoped<IJobCheckpointRepository, JobCheckpointRepository>();
        services.AddScoped<ISyncCheckpointRepository, SyncCheckpointRepository>();
        services.AddScoped<IJobScheduleRepository, JobScheduleRepository>();
        services.AddScoped<IJobTypeConfigurationRepository, JobTypeConfigurationRepository>();
        services.AddScoped<IOrdersBackfillStateRepository, OrdersBackfillStateRepository>();
        services.AddScoped<IWildberriesMethodThrottleRepository, WildberriesMethodThrottleRepository>();
        services.AddScoped<IRawApiResponseRepository, RawApiResponseRepository>();
        services.AddScoped<ISystemChannelTemplateRepository, SystemChannelTemplateRepository>();
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        services.AddScoped<ProductsImportOrchestrator>();
        services.AddScoped<ProductsImportBatchAggregator>();
        services.AddScoped<OrdersImportCheckpointStore>();
        services.AddScoped<OrdersBatchPublisher>();
        services.AddScoped<OrdersBackfillWalker>();
        services.AddScoped<BatchImportFinalizer>();
        services.AddScoped<OrdersImportWatermarkWriter>();
        services.AddScoped<OrdersBatchAggregator>();

        services.AddSingleton<IChannelCatalogService, ChannelCatalogService>();
        services.AddChannelModule<WildberriesChannelModule>();
        services.AddChannelModule<OzonChannelModule>();

        services.AddScoped<IOrdersFetcherResolver, OrdersFetcherResolver>();

        services.AddScoped<OrdersContinuationScheduler>();
        services.AddScoped<OrdersImportOrchestrator>();
        services.AddScoped<OrdersImportFinisher>();

        // Creates the orders JobSchedule for a connection added while the application is running.
        // The consumer implements IConsumer and is registered by AddIntegrationsConsumers, which
        // scans this assembly.
        services.AddScoped<ConnectionCreatedConsumer>();
        services.AddScoped<ConnectionScheduleProvisioner>();

        services.AddScoped<JobRunLifecycle>();
        services.AddScoped<JobRunStarter>();
        services.AddScoped<IConnectionResolver, ConnectionResolver>();
        services.AddScoped<IEndpointResolver, EndpointResolver>();

        // AsyncLocal-ambient контекст текущего JobRun; читается RawApiResponseCaptureHandler,
        // чтобы привязать перехваченный сырой ответ к задаче синхронизации, которая его вызвала.
        services.AddScoped<JobRunExecutionContext>();
        services.AddScoped<IJobRunExecutionContext>(sp => sp.GetRequiredService<JobRunExecutionContext>());
        services.AddScoped<IJobRunExecutionScope>(sp => sp.GetRequiredService<JobRunExecutionContext>());

        services.AddTransient<OzonAuthHandler>();
        services.AddTransient<WildberriesAuthHandler>();
        services.AddTransient<YandexKitAuthHandler>();

        // Перехватывает и сохраняет сырые request/response каждого маркетплейса в
        // RawApiResponses. Зарегистрирован один раз и подключается к пайплайну всех каналов
        // ниже, чтобы запись аудита не дублировалась в каждом API-клиенте или консьюмере.
        services.AddTransient<RawApiResponseCaptureHandler>();

        // Wildberries and Ozon clients, rate limiters and validators are registered by their
        // channel modules (see AddChannelModule calls above).
        services.AddHttpClient<YandexKitApiClient>()
            .AddHttpMessageHandler<YandexKitAuthHandler>()
            .AddHttpMessageHandler<RawApiResponseCaptureHandler>()
            .AddResilienceHandler("retry-policy", (builder, context) =>
             {
                 ILogger logger = context.ServiceProvider.GetRequiredService<ILogger<YandexKitApiClient>>();
                 ResiliencePipeline<HttpResponseMessage> retryPipeline = RetryPolicies.GetRetryPolicy(logger);
                 builder.AddPipeline(retryPipeline);
             });

        // Валидаторы ключей — без retry, таймаут 10 сек.
        // Wildberries and Ozon validators are registered by their channel modules.
        services.AddHttpClient<YandexKitApiKeyValidator>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddKeyedTransient<IChannelApiClient>(
            "yandex.kit", (sp, _) => sp.GetRequiredService<YandexKitApiClient>());
        services.AddKeyedTransient<IApiKeyValidator>(
            "yandex.kit", (sp, _) => sp.GetRequiredService<YandexKitApiKeyValidator>());

        services.AddTransient<IApiKeyValidatorFactory, ApiKeyValidatorFactory>();
        services.AddTransient<IChannelApiClientFactory, ChannelApiClientFactory>();

        // Products sync is still Wildberries-only. Orders are NOT registered this way: an orders
        // run must reach the client of its own connection's channel, and it gets one through
        // IChannelApiClientFactory (see OrdersImportOrchestrator).
        services.AddScoped<IProductsSource>(sp => sp.GetRequiredService<WildberriesApiClient>());

        return services;
    }

    /// <summary>
    /// Registers Integrations module MassTransit consumers.
    /// </summary>
    /// <param name="configurator">MassTransit bus registration configurator.</param>
    public static void AddIntegrationsConsumers(this IBusRegistrationConfigurator configurator)
    {
        configurator.AddConsumers(Assembly.GetExecutingAssembly());
    }
}
