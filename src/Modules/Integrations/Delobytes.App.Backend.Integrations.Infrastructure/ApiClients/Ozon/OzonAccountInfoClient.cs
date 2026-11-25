using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Delobytes.App.Backend.Integrations.Application.Models;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;

/// <summary>
/// Reads the seller identity Ozon reports for a raw API key. The connection this validates is not
/// persisted yet, so the call cannot use the typed client's <see cref="OzonAuthHandler"/>: that handler
/// resolves credentials from a stored Connection by id, and there is none to resolve.
/// </summary>
internal sealed class OzonAccountInfoClient
{
    private const string SellerInfoPath = "/v1/seller/info";
    private const string SellerIdSetting = "sellerId";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private readonly OzonEndpoint _endpoint;

    public OzonAccountInfoClient(IHttpClientFactory httpClientFactory, ILogger logger, OzonEndpoint endpoint)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _endpoint = endpoint;
    }

    public async Task<AccountInfo?> GetAccountInfoAsync(
        string apiKey,
        Dictionary<string, string>? settings,
        CancellationToken cancellationToken)
    {
        if (settings == null || !settings.TryGetValue(SellerIdSetting, out string? sellerId))
        {
            _logger.LogWarning("Cannot retrieve Ozon account info: sellerId is missing.");
            return null;
        }

        using HttpClient client = _httpClientFactory.CreateClient();

        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_endpoint.BaseUrl}{SellerInfoPath}");

        request.Headers.TryAddWithoutValidation("Client-Id", sellerId);
        request.Headers.TryAddWithoutValidation("Api-Key", apiKey);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Ozon seller/info returned {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            OzonSellerInfoResponse? body =
                await response.Content.ReadFromJsonAsync<OzonSellerInfoResponse>(cancellationToken);

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
