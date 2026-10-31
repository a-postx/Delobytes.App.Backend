using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// Ozon marketplace API client for orders and stocks synchronisation.
/// </summary>
public class OzonApiClient : IChannelApiClient, IProductsSource
{
    private const string DefaultApiBaseUrl = "https://api-seller.ozon.ru";

    private readonly HttpClient _httpClient;

    // только для метода GetAccountInfoAsync
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OzonApiClient> _logger;

    private Guid? _templateId;
    private string? _apiBaseUrl;

    /// <inheritdoc/>
    public string ChannelCode => "ozon";

    /// <summary>
    /// Gets the base URL of the bound system channel template, falling back to the Ozon public
    /// endpoint. Exposed for tests asserting that the factory binds the template.
    /// </summary>
    internal string BaseUrl => _apiBaseUrl ?? DefaultApiBaseUrl;

    /// <summary>
    /// Gets the identifier of the bound system channel template, or null when the client was not
    /// resolved through the factory.
    /// </summary>
    internal Guid? TemplateId => _templateId;

    public OzonApiClient(
        HttpClient httpClient,
        IHttpClientFactory httpClientFactory,
        ILogger<OzonApiClient> logger)
    {
        _httpClient = httpClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Binds the client to a system channel template. Called by
    /// <see cref="ChannelApiClientFactory"/>, never by the calling code.
    /// </summary>
    /// <param name="template">Channel template of the Ozon system channel.</param>
    public void SetTemplate(SystemChannelTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        _templateId = template.Id;
        _apiBaseUrl = string.IsNullOrWhiteSpace(template.ApiBaseUrl)
            ? DefaultApiBaseUrl
            : template.ApiBaseUrl;
    }

    /// <inheritdoc/>
    public async Task<AccountInfo?> GetAccountInfoAsync(
        Guid? connectionId,
        string apiKey,
        string? apiSecret,
        Dictionary<string, string>? settings,
        CancellationToken ct)
    {
        if (settings == null || !settings.TryGetValue("sellerId", out string? sellerId))
        {
            _logger.LogWarning("Cannot retrieve Ozon account info: sellerId is missing.");
            return null;
        }

        using HttpClient client = _httpClientFactory.CreateClient();

        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api-seller.ozon.ru/v1/seller/info");

        request.Headers.TryAddWithoutValidation("Client-Id", sellerId);
        request.Headers.TryAddWithoutValidation("Api-Key", apiKey);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Ozon seller/info returned {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            OzonSellerInfoResponse? body =
                await response.Content.ReadFromJsonAsync<OzonSellerInfoResponse>(ct);

            if (body is null)
            {
                return null;
            }

            return new AccountInfo
            {
                CustomerName = body.Company?.Name,
                LegalName = body.Company?.LegalName,
                Inn = body.Company?.Inn,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to retrieve Ozon seller info.");
            return null;
        }
    }

    public Task<ApiResponse<ProductCardsData>> GetProductCardsAsync(
        Guid connectionId, ProductCardsCursor? cursor, int limit, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    ///
    /// <remarks>Ozon does not support product cards sync yet.</remarks>
    public Task<ProductCardsPage> GetProductCardsAsync(
        Guid connectionId, ProductCardsPageRequest request, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Ozon does not support product cards sync yet.");
    }

    private sealed class OzonSellerInfoResponse
    {
        [JsonPropertyName("company")]
        public OzonCompanyInfo? Company { get; init; }
    }

    private sealed class OzonCompanyInfo
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("legal_name")]
        public string? LegalName { get; init; }

        [JsonPropertyName("inn")]
        public string? Inn { get; init; }
    }
}
