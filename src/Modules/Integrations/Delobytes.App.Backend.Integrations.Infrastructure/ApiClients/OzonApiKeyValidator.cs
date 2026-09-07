using System.Net;
using System.Text;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

public class OzonApiKeyValidator : IApiKeyValidator
{
    private readonly HttpClient _httpClient;

    public OzonApiKeyValidator(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ApiKeyValidationResult> ValidateAsync(
        string apiKey,
        string? apiSecret,
        Dictionary<string, string>? settings,
        CancellationToken ct)
    {
        if (settings == null || !settings.TryGetValue("sellerId", out string? sellerId))
        {
            return ApiKeyValidationResult.Failure("Для Ozon необходимо указать Client ID.");
        }

        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api-seller.ozon.ru/v3/product/list");

        request.Headers.TryAddWithoutValidation("Client-Id", sellerId);
        request.Headers.TryAddWithoutValidation("Api-Key", apiKey);
        request.Content = new StringContent("{ \"filter\": { \"visibility\": \"ALL\" }, \"last_id\": \"\", \"limit\": 1 }", Encoding.UTF8, "application/json");

        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                return ApiKeyValidationResult.Success();
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return ApiKeyValidationResult.Failure("Неверный API-ключ или Client ID Ozon.");
            }

            return ApiKeyValidationResult.Failure(
                $"Ozon вернул неожиданный ответ: {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiKeyValidationResult.Failure(
                "Не удалось подключиться к Ozon. Попробуйте позже.");
        }
    }
}
