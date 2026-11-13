using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Application.Orders;
using Delobytes.App.Backend.Integrations.Application.Products;
using Delobytes.App.Backend.Integrations.Application.SalesChannels;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.RateLimiting;
using Delobytes.App.Backend.Integrations.Infrastructure.Policies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace Delobytes.App.Backend.Integrations.Infrastructure.SalesChannels;

public sealed class OzonChannelModule : ChannelModule
{
    public override ChannelDescriptor Descriptor
    {
        get
        {
            return ChannelCatalog.All.Single(
                descriptor => string.Equals(descriptor.Code, ChannelCatalog.OzonCode, StringComparison.Ordinal));
        }
    }

    public override IReadOnlyList<(JobType JobType, ChannelJobKind Kind)> JobTypes { get; } = new[]
    {
        (JobType.OzonOrdersImportFbo, ChannelJobKind.OrdersImport),
        (JobType.OzonOrdersImportFbs, ChannelJobKind.OrdersImport),
        (JobType.OzonProductsImport, ChannelJobKind.ProductsImport),
    };

    public override string? GetRateLimitMethod(JobType jobType)
    {
        return jobType switch
        {
            JobType.OzonOrdersImportFbo => OrdersRateLimiterMethods.OzonPostingFboList,
            JobType.OzonOrdersImportFbs => OrdersRateLimiterMethods.OzonPostingFbsList,
            _ => null,
        };
    }

    public override void RegisterProducts(IServiceCollection services)
    {
        // 1000 is the endpoint's ceiling and keeps the run short; 200 keeps one published message
        // near 1-3 MB, because a page of full cards with attributes is 3-15 MB in total.
        services.AddKeyedScoped<ProductsImportTuning>(
            ChannelCatalog.OzonCode,
            (sp, _) => new ProductsImportTuning(1000, 200));

        services.AddKeyedScoped<IProductsSource>(
            ChannelCatalog.OzonCode,
            (sp, _) => sp.GetRequiredService<OzonApiClient>());
    }

    public override void RegisterOrders(IServiceCollection services)
    {
        AddOrdersJob(services, JobType.OzonOrdersImportFbo, OrdersSchema.Fbo, OzonPostingStatuses.FboStatuses);
        AddOrdersJob(services, JobType.OzonOrdersImportFbs, OrdersSchema.Fbs, OzonPostingStatuses.FbsStatuses);
    }

    public override void RegisterApiClient(IServiceCollection services)
    {
        services.AddKeyedSingleton<IChannelRateLimiter>(ChannelCatalog.OzonCode, (sp, _) =>
        {
            OzonImportOptions ozonImportOptions = sp.GetRequiredService<IOptions<OzonImportOptions>>().Value;

            return new OzonRateLimiter(
                sp.GetRequiredService<TimeProvider>(), ozonImportOptions.ResolveMinimumRequestInterval());
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

        services.AddKeyedTransient<IChannelApiClient>(
            ChannelCatalog.OzonCode,
            (sp, _) => sp.GetRequiredService<OzonApiClient>());
    }

    public override void RegisterApiKeyValidator(IServiceCollection services)
    {
        services.AddHttpClient<OzonApiKeyValidator>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddKeyedTransient<IApiKeyValidator>(
            ChannelCatalog.OzonCode,
            (sp, _) => sp.GetRequiredService<OzonApiKeyValidator>());
    }

    private static void AddOrdersJob(
        IServiceCollection services, JobType jobType, OrdersSchema schema, IReadOnlyList<string> statuses)
    {
        string fetchersKey = OrdersFetcherKeys.Fetchers(ChannelCatalog.OzonCode, jobType);
        string readerKey = OrdersFetcherKeys.Reader(
            ChannelCatalog.OzonCode, jobType, OrdersFetcherKeys.ReaderSuffix);

        services.AddKeyedScoped<OrdersImportTuning>(fetchersKey, (sp, _) => GetOzonTuning(sp, jobType));
        services.AddKeyedScoped<PacedOrdersSource>(ChannelCatalog.OzonCode, (sp, _) => new PacedOrdersSource(
            sp.GetRequiredKeyedService<IChannelRateLimiter>(ChannelCatalog.OzonCode),
            sp.GetRequiredService<TimeProvider>()));
        services.AddKeyedScoped<CursorPagedOrdersReader>(readerKey, (sp, _) => new CursorPagedOrdersReader(
            sp.GetRequiredKeyedService<PacedOrdersSource>(ChannelCatalog.OzonCode),
            sp.GetRequiredService<ILogger<CursorPagedOrdersReader>>(),
            sp.GetRequiredKeyedService<OrdersImportTuning>(fetchersKey),
            schema));
        services.AddKeyedScoped<IOrdersFetcher>(
            OrdersFetcherKeys.Backfill(ChannelCatalog.OzonCode, jobType),
            (sp, _) => new BackfillOrdersFetcher(
                sp.GetRequiredService<OrdersBackfillWalker>(),
                sp.GetRequiredKeyedService<CursorPagedOrdersReader>(readerKey)));
        services.AddKeyedScoped<IOrdersFetcher>(
            OrdersFetcherKeys.Delta(ChannelCatalog.OzonCode, jobType),
            (sp, _) => new OzonDeltaOrdersFetcher(
                sp.GetRequiredKeyedService<CursorPagedOrdersReader>(readerKey),
                sp.GetRequiredService<ISyncCheckpointRepository>(),
                sp.GetRequiredService<OrdersBatchPublisher>(),
                sp.GetRequiredService<ILogger<OzonDeltaOrdersFetcher>>(),
                sp.GetRequiredKeyedService<OrdersImportTuning>(fetchersKey),
                schema,
                statuses));
        services.AddKeyedScoped<IOrdersFetchers>(fetchersKey, (sp, _) => new OrdersFetchers(
            sp.GetRequiredKeyedService<IOrdersFetcher>(
                OrdersFetcherKeys.Backfill(ChannelCatalog.OzonCode, jobType)),
            sp.GetRequiredKeyedService<IOrdersFetcher>(
                OrdersFetcherKeys.Delta(ChannelCatalog.OzonCode, jobType)),
            sp.GetRequiredKeyedService<OrdersImportTuning>(fetchersKey)));
    }

    private static OrdersImportTuning GetOzonTuning(IServiceProvider serviceProvider, JobType jobType)
    {
        return serviceProvider.GetRequiredService<IOptions<OzonImportOptions>>()
            .Value.ToTuning(jobType);
    }
}
