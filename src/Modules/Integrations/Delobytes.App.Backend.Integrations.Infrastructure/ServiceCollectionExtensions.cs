using System.Reflection;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;
using Delobytes.App.Backend.Integrations.Infrastructure.Policies;
using Delobytes.App.Backend.Integrations.Infrastructure.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        services.Configure<WildberriesImportOptions>(
            configuration?.GetSection("WildberriesImport") ?? new ConfigurationBuilder().Build().GetSection("WildberriesImport"));

        services.AddDbContext<IntegrationsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.MigrationsHistoryTable("__IntegrationsMigrationsHistory", "integrations")));

        services.AddMemoryCache();

        services.AddScoped<IConnectionRepository, ConnectionRepository>();
        services.AddScoped<ISyncJobRepository, SyncJobRepository>();
        services.AddScoped<IRawApiResponseRepository, RawApiResponseRepository>();
        services.AddScoped<ISystemChannelTemplateRepository, SystemChannelTemplateRepository>();
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        services.AddScoped<ProcessSyncJobConsumer>();
        services.AddScoped<ProcessProductsImportConsumer>();
        services.AddScoped<ProcessProductImportBatchCompletedConsumer>();

        services.AddScoped<IConnectionResolver, ConnectionResolver>();
        services.AddScoped<IEndpointResolver, EndpointResolver>();

        // AsyncLocal-ambient контекст текущего SyncJob; читается RawApiResponseCaptureHandler,
        // чтобы привязать перехваченный сырой ответ к задаче синхронизации, которая его вызвала.
        services.AddScoped<SyncJobExecutionContext>();
        services.AddScoped<ISyncJobExecutionContext>(sp => sp.GetRequiredService<SyncJobExecutionContext>());

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
