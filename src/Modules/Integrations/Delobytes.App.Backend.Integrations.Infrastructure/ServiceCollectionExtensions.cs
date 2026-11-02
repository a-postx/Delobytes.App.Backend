using System.Reflection;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.RateLimiting;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;
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
        services.AddScoped<IWildberriesRateLimiter, WildberriesRateLimiter>();
        services.AddScoped<WildberriesOrdersRateLimiterAdapter>();
        services.AddSingleton<OzonRateLimiter>(sp =>
        {
            OzonImportOptions ozonImportOptions = sp.GetRequiredService<IOptions<OzonImportOptions>>().Value;
            TimeSpan minimumRequestInterval = TimeSpan.FromSeconds(ozonImportOptions.MinimumRequestIntervalSeconds);
            return new OzonRateLimiter(sp.GetRequiredService<TimeProvider>(), minimumRequestInterval);
        });
        services.AddScoped<IOrdersRateLimiter, OrdersRateLimiterRouter>();
        services.AddScoped<IRawApiResponseRepository, RawApiResponseRepository>();
        services.AddScoped<ISystemChannelTemplateRepository, SystemChannelTemplateRepository>();
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        services.AddScoped<ProcessProductsImportConsumer>();
        services.AddScoped<ProcessProductImportBatchCompletedConsumer>();
        services.AddScoped<OrdersImportCheckpointStore>();
        services.AddScoped<OrdersBatchPublisher>();
        services.AddScoped<PacedOrdersSource>();
        services.AddScoped<BisectingOrdersReader>();
        services.AddScoped(sp =>
        {
            int waitSeconds = sp.GetRequiredService<IOptions<WildberriesImportOptions>>().Value.MaxInProcessWaitSeconds;

            return new SinglePageOrdersReader(
                sp.GetRequiredService<PacedOrdersSource>(),
                sp.GetRequiredService<ILogger<SinglePageOrdersReader>>(),
                OrdersRateLimiterMethods.WildberriesSupplierOrders,
                TimeSpan.FromSeconds(waitSeconds >= 0 ? waitSeconds : WildberriesImportOptions.DefaultMaxInProcessWaitSeconds));
        });
        services.AddScoped<OrdersBackfillWalker>();
        services.AddScoped(sp => new BackfillOrdersFetcher(
            sp.GetRequiredService<OrdersBackfillWalker>(),
            sp.GetRequiredService<SinglePageOrdersReader>()));
        services.AddScoped<DeltaOrdersFetcher>();

        // The Ozon pager is bound to one method and one wait tolerance at construction, so it is
        // built here rather than resolved by interface: registering it as IOrdersWindowReader
        // would collide with BisectingOrdersReader, the other implementation of that interface.
        services.AddScoped(sp =>
        {
            OzonImportOptions ozonOptions = sp.GetRequiredService<IOptions<OzonImportOptions>>().Value;

            return new CursorPagedOrdersReader(
                sp.GetRequiredService<PacedOrdersSource>(),
                sp.GetRequiredService<ILogger<CursorPagedOrdersReader>>(),
                TimeSpan.FromSeconds(ozonOptions.MaxInProcessWaitSeconds >= 0
                    ? ozonOptions.MaxInProcessWaitSeconds
                    : OzonImportOptions.DefaultMaxInProcessWaitSeconds),
                OrdersRateLimiterMethods.OzonPostingFboList);
        });
        services.AddScoped(sp => new OzonBackfillOrdersFetcher(
            sp.GetRequiredService<OrdersBackfillWalker>(),
            sp.GetRequiredService<CursorPagedOrdersReader>()));
        services.AddScoped(sp => new OzonDeltaOrdersFetcher(
            sp.GetRequiredService<CursorPagedOrdersReader>(),
            sp.GetRequiredService<ISyncCheckpointRepository>(),
            sp.GetRequiredService<OrdersBatchPublisher>(),
            sp.GetRequiredService<ILogger<OzonDeltaOrdersFetcher>>(),
            sp.GetRequiredService<IOptions<OzonImportOptions>>()));
        services.AddScoped(sp => new OzonOrdersFetchers(
            sp.GetRequiredService<OzonBackfillOrdersFetcher>(),
            sp.GetRequiredService<OzonDeltaOrdersFetcher>(),
            sp.GetRequiredService<CursorPagedOrdersReader>(),
            sp.GetRequiredService<IOptions<OzonImportOptions>>()));
        services.AddScoped(sp => new WildberriesOrdersFetchers(
            sp.GetRequiredService<BackfillOrdersFetcher>(),
            sp.GetRequiredService<DeltaOrdersFetcher>(),
            sp.GetRequiredService<BisectingOrdersReader>(),
            sp.GetRequiredService<IOptions<WildberriesImportOptions>>()));
        services.AddScoped<IOrdersFetcherResolver, OrdersFetcherResolver>();

        services.AddScoped<OrdersContinuationScheduler>();
        services.AddScoped<ProcessOrdersImportConsumer>();
        services.AddScoped<ProcessOrdersBatchImportCompletedConsumer>();

        // Creates the orders JobSchedule for a connection added while the application is running.
        // The MassTransit adapter (ConnectionCreatedEventMassTransitConsumer) is registered by
        // AddIntegrationsConsumers, which scans this assembly.
        services.AddScoped<ConnectionCreatedEventConsumer>();

        services.AddScoped<JobRunLifecycle>();
        services.AddScoped<IConnectionResolver, ConnectionResolver>();
        services.AddScoped<IEndpointResolver, EndpointResolver>();

        // AsyncLocal-ambient контекст текущего JobRun; читается RawApiResponseCaptureHandler,
        // чтобы привязать перехваченный сырой ответ к задаче синхронизации, которая его вызвала.
        services.AddScoped<JobRunExecutionContext>();
        services.AddScoped<IJobRunExecutionContext>(sp => sp.GetRequiredService<JobRunExecutionContext>());

        services.AddTransient<OzonAuthHandler>();
        services.AddTransient<WildberriesAuthHandler>();
        services.AddTransient<YandexKitAuthHandler>();

        // Перехватывает и сохраняет сырые request/response каждого маркетплейса в
        // RawApiResponses. Зарегистрирован один раз и подключается к пайплайну всех каналов
        // ниже, чтобы запись аудита не дублировалась в каждом API-клиенте или консьюмере.
        services.AddTransient<RawApiResponseCaptureHandler>();

        // WildberriesApiClient для операций синхронизации — с retry
        services.AddHttpClient<WildberriesApiClient>()
            .AddHttpMessageHandler<WildberriesAuthHandler>()
            .AddHttpMessageHandler<RawApiResponseCaptureHandler>()
            .AddResilienceHandler("retry-policy", (builder, context) =>
            {
                ILogger logger = context.ServiceProvider.GetRequiredService<ILogger<WildberriesApiClient>>();
                ResiliencePipeline<HttpResponseMessage> retryPipeline = RetryPolicies.GetRetryPolicy(logger);
                builder.AddPipeline(retryPipeline);
            });

        services.AddHttpClient<OzonApiClient>()
            .AddHttpMessageHandler<OzonAuthHandler>()
            .AddHttpMessageHandler<RawApiResponseCaptureHandler>()
            .AddResilienceHandler("retry-policy", (builder, context) =>
            {
                ILogger logger = context.ServiceProvider.GetRequiredService<ILogger<OzonApiClient>>();
                ResiliencePipeline<HttpResponseMessage> retryPipeline = RetryPolicies.GetRetryPolicy(logger);
                builder.AddPipeline(retryPipeline);
            });

        services.AddHttpClient<YandexKitApiClient>()
            .AddHttpMessageHandler<YandexKitAuthHandler>()
            .AddHttpMessageHandler<RawApiResponseCaptureHandler>()
            .AddResilienceHandler("retry-policy", (builder, context) =>
             {
                 ILogger logger = context.ServiceProvider.GetRequiredService<ILogger<YandexKitApiClient>>();
                 ResiliencePipeline<HttpResponseMessage> retryPipeline = RetryPolicies.GetRetryPolicy(logger);
                 builder.AddPipeline(retryPipeline);
             });

        // Валидаторы ключей — без retry, таймаут 10 сек
        services.AddHttpClient<WildberriesApiKeyValidator>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddHttpClient<OzonApiKeyValidator>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddHttpClient<YandexKitApiKeyValidator>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddTransient<IApiKeyValidatorFactory, ApiKeyValidatorFactory>();
        services.AddTransient<IChannelApiClientFactory, ChannelApiClientFactory>();

        // Products sync is still Wildberries-only. Orders are NOT registered this way: an orders
        // run must reach the client of its own connection's channel, and it gets one through
        // IChannelApiClientFactory (see ProcessOrdersImportConsumer).
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
