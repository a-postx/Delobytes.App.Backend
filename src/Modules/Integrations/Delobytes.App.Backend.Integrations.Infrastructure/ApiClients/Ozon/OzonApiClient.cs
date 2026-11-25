using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.SalesChannels;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;

/// <summary>
/// Ozon marketplace API client. Reads FBO and FBS orders, the full catalogue of on-sale product cards
/// through the attributes endpoint, and the daily finance accruals.
/// </summary>
/// <remarks>
/// The class is the composition root of the Ozon client: it keeps the constructor the DI container and
/// the tests already use, and hands every capability its own collaborator over one shared
/// <see cref="OzonRequestPipeline"/> and one <see cref="OzonEndpoint"/>. A capability that grows (reviews,
/// supplies) becomes a sibling client without touching this signature or the channel registration.
/// </remarks>
public class OzonApiClient : IChannelApiClient, IProductsSource, IOrdersSource, IChannelApiClientTemplateBinder, IFinanceEntriesSource
{
    private readonly OzonEndpoint _endpoint;
    private readonly OzonAccountInfoClient _accountInfo;
    private readonly OzonOrdersClient _orders;
    private readonly OzonProductsClient _products;
    private readonly OzonFinanceClient _finance;

    /// <summary>
    /// Initializes a new instance of the <see cref="OzonApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">Typed HTTP client carrying <see cref="OzonAuthHandler"/>.</param>
    /// <param name="httpClientFactory">Factory used by <see cref="GetAccountInfoAsync"/> only.</param>
    /// <param name="connectionResolver">
    /// Resolves the connection the request authenticates as, so the rate-limit scope is keyed by the
    /// key Ozon actually meters rather than by an arbitrary connection of the tenant.
    /// </param>
    /// <param name="rateLimiter">
    /// One limiter per API key, shared by orders, products and finance: Ozon meters the key, not the
    /// method.
    /// </param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="timeProvider">
    /// Clock of the pacing helper and of the response timestamps. Both must read the same clock as the
    /// limiter, otherwise a slot that is already due looks like one that is still held.
    /// </param>
    public OzonApiClient(
        HttpClient httpClient,
        IHttpClientFactory httpClientFactory,
        IConnectionResolver connectionResolver,
        [FromKeyedServices(ChannelCatalog.OzonCode)] IChannelRateLimiter rateLimiter,
        ILogger<OzonApiClient> logger,
        TimeProvider timeProvider)
    {
        _endpoint = new OzonEndpoint();

        OzonRequestPipeline pipeline = new OzonRequestPipeline(
            httpClient,
            connectionResolver,
            rateLimiter,
            logger,
            timeProvider,
            _endpoint);

        _accountInfo = new OzonAccountInfoClient(httpClientFactory, logger, _endpoint);
        _orders = new OzonOrdersClient(pipeline, logger);
        _products = new OzonProductsClient(pipeline, logger, timeProvider);
        _finance = new OzonFinanceClient(pipeline, logger);
    }

    /// <inheritdoc/>
    public string ChannelCode => ChannelCatalog.OzonCode;

    /// <summary>
    /// Gets the base URL of the bound system channel template, falling back to the Ozon public
    /// endpoint. Exposed for tests asserting that the factory binds the template.
    /// </summary>
    internal string BaseUrl => _endpoint.BaseUrl;

    /// <summary>
    /// Gets the identifier of the bound system channel template, or null when the client was not
    /// resolved through the factory.
    /// </summary>
    internal Guid? TemplateId => _endpoint.TemplateId;

    /// <summary>
    /// Binds the client to a system channel template. Called by
    /// <see cref="ChannelApiClientFactory"/>, never by the calling code.
    /// </summary>
    /// <param name="template">Channel template of the Ozon system channel.</param>
    public void SetTemplate(SystemChannelTemplate template)
    {
        _endpoint.Bind(template);
    }

    /// <inheritdoc/>
    public Task<AccountInfo?> GetAccountInfoAsync(
        Guid? connectionId,
        string apiKey,
        string? apiSecret,
        Dictionary<string, string>? settings,
        CancellationToken ct)
    {
        return _accountInfo.GetAccountInfoAsync(apiKey, settings, ct);
    }

    /// <inheritdoc/>
    public Task<OrdersPage> GetOrdersAsync(
        Guid connectionId, OrdersRequest request, CancellationToken cancellationToken)
    {
        return _orders.GetOrdersAsync(connectionId, request, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ApiResponse<ProductCardsData>> GetProductCardsAsync(
        Guid connectionId, ProductCardsCursor? cursor, int limit, CancellationToken ct)
    {
        return _products.GetProductCardsAsync(connectionId, cursor, limit, ct);
    }

    /// <inheritdoc/>
    public Task<ProductCardsPage> GetProductCardsAsync(
        Guid connectionId, ProductCardsPageRequest request, CancellationToken cancellationToken)
    {
        return _products.GetProductCardsAsync(connectionId, request, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<FinanceEntriesPage> GetFinanceEntriesAsync(
        Guid connectionId,
        FinanceEntriesRequest request,
        CancellationToken cancellationToken)
    {
        return _finance.GetFinanceEntriesAsync(connectionId, request, cancellationToken);
    }
}
