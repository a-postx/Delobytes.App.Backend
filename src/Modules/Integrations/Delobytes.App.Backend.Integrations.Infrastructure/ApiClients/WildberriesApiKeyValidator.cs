using System.Net;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;

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
            HttpMethod.Get,
            "https://suppliers-api.wildberries.ru/api/v3/offices");

        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

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
