using System.Net;
using System.Net.Http.Json;
using System.Text;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Polly;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

public class WildberriesApiKeyValidator : IApiKeyValidator
{
    private readonly HttpClient _httpClient;

    public WildberriesApiKeyValidator(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ApiKeyValidationResult> ValidateAsync(
        string apiKey,
        string? apiSecret,
        Dictionary<string, string>? settings,
        CancellationToken ct)
    {
        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://content-api.wildberries.ru/content/v2/get/cards/list");

        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        string body = "{ \"settings\": { \"sort\": { \"ascending\": true }, \"cursor\": { \"limit\": 1 }, \"filter\": { \"withPhoto\": -1 } } }";
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                return ApiKeyValidationResult.Success();
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return ApiKeyValidationResult.Failure(
                    "Неверный API-ключ Wildberries. Убедитесь, что ключ активен и имеет нужные права.");
            }

            return ApiKeyValidationResult.Failure(
                $"Wildberries вернул неожиданный ответ: {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiKeyValidationResult.Failure(
                "Не удалось подключиться к Wildberries. Попробуйте позже.");
        }
    }
}
