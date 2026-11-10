using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Application.Orders;
using Delobytes.App.Backend.Integrations.Application.SalesChannels;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.RateLimiting;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Wildberries;
using Delobytes.App.Backend.Integrations.Infrastructure.Policies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace Delobytes.App.Backend.Integrations.Infrastructure.SalesChannels;

public sealed class WildberriesChannelModule : ChannelModule
{
    private const string ValidatorHttpClientName = nameof(WildberriesApiKeyValidator);

    public override ChannelDescriptor Descriptor
    {
        get
        {
            return ChannelCatalog.All.Single(
                descriptor => string.Equals(descriptor.Code, ChannelCatalog.WildberriesCode, StringComparison.Ordinal));
        }
    }

    public override void RegisterOrders(IServiceCollection services)
    {
        JobType jobType = JobType.WbOrdersImport;
        string fetchersKey = OrdersFetcherKeys.Fetchers(ChannelCatalog.WildberriesCode, jobType);
        string backfillReaderKey = OrdersFetcherKeys.Reader(
            ChannelCatalog.WildberriesCode, jobType, OrdersFetcherKeys.BackfillSuffix);
        string deltaReaderKey = OrdersFetcherKeys.Reader(
            ChannelCatalog.WildberriesCode, jobType, OrdersFetcherKeys.DeltaSuffix);

        services.AddKeyedScoped<OrdersImportTuning>(fetchersKey, (sp, _) => GetWildberriesTuning(sp));
        services.AddKeyedScoped<PacedOrdersSource>(ChannelCatalog.WildberriesCode, (sp, _) => new PacedOrdersSource(
            sp.GetRequiredKeyedService<IChannelRateLimiter>(ChannelCatalog.WildberriesCode),
            sp.GetRequiredService<TimeProvider>()));
        services.AddKeyedScoped<SinglePageOrdersReader>(backfillReaderKey, (sp, _) => new SinglePageOrdersReader(
            sp.GetRequiredKeyedService<PacedOrdersSource>(ChannelCatalog.WildberriesCode),
            sp.GetRequiredService<ILogger<SinglePageOrdersReader>>()));
        services.AddKeyedScoped<BisectingOrdersReader>(deltaReaderKey, (sp, _) => new BisectingOrdersReader(
            sp.GetRequiredKeyedService<PacedOrdersSource>(ChannelCatalog.WildberriesCode),
            sp.GetRequiredService<ILogger<BisectingOrdersReader>>(),
            sp.GetRequiredKeyedService<OrdersImportTuning>(fetchersKey)));
        services.AddKeyedScoped<IOrdersFetcher>(
            OrdersFetcherKeys.Backfill(ChannelCatalog.WildberriesCode, jobType),
            (sp, _) => new BackfillOrdersFetcher(
                sp.GetRequiredService<OrdersBackfillWalker>(),
                sp.GetRequiredKeyedService<SinglePageOrdersReader>(backfillReaderKey)));
        services.AddKeyedScoped<IOrdersFetcher>(
            OrdersFetcherKeys.Delta(ChannelCatalog.WildberriesCode, jobType),
            (sp, _) => new DeltaOrdersFetcher(
                sp.GetRequiredKeyedService<BisectingOrdersReader>(deltaReaderKey),
                sp.GetRequiredService<ISyncCheckpointRepository>(),
                sp.GetRequiredService<OrdersBatchPublisher>(),
                sp.GetRequiredService<ILogger<DeltaOrdersFetcher>>()));
        services.AddKeyedScoped<IOrdersFetchers>(fetchersKey, (sp, _) => new OrdersFetchers(
            sp.GetRequiredKeyedService<IOrdersFetcher>(
                OrdersFetcherKeys.Backfill(ChannelCatalog.WildberriesCode, jobType)),
            sp.GetRequiredKeyedService<IOrdersFetcher>(
                OrdersFetcherKeys.Delta(ChannelCatalog.WildberriesCode, jobType)),
            sp.GetRequiredKeyedService<OrdersImportTuning>(fetchersKey)));
    }

    public override void RegisterApiClient(IServiceCollection services)
    {
        services.AddScoped<WildberriesRateLimiter>();

        // One contract, one registration: orders consumers, the scheduler pre-check, product
        // cards and the validator all resolve this same keyed limiter.
        services.AddKeyedScoped<IChannelRateLimiter, WildberriesRateLimiter>(ChannelCatalog.WildberriesCode);

        services.AddHttpClient<WildberriesApiClient>()
            .AddHttpMessageHandler<WildberriesAuthHandler>()
            .AddHttpMessageHandler<RawApiResponseCaptureHandler>()
            .AddResilienceHandler("retry-policy", (builder, context) =>
            {
                ILogger logger = context.ServiceProvider.GetRequiredService<ILogger<WildberriesApiClient>>();
                ResiliencePipeline<HttpResponseMessage> retryPipeline = RetryPolicies.GetRetryPolicy(logger);
                builder.AddPipeline(retryPipeline);
            });

        services.AddKeyedTransient<IChannelApiClient>(
            ChannelCatalog.WildberriesCode,
            (sp, _) => sp.GetRequiredService<WildberriesApiClient>());
    }

    public override void RegisterApiKeyValidator(IServiceCollection services)
    {
        services.AddHttpClient(ValidatorHttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddKeyedTransient<IApiKeyValidator>(
            ChannelCatalog.WildberriesCode,
            (sp, _) => new WildberriesApiKeyValidator(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(ValidatorHttpClientName),
                sp.GetRequiredKeyedService<IChannelRateLimiter>(ChannelCatalog.WildberriesCode)));
    }

    private static OrdersImportTuning GetWildberriesTuning(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<IOptions<WildberriesImportOptions>>()
            .Value.ToTuning(JobType.WbOrdersImport);
    }
}
