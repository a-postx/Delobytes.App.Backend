using System.Net;
using System.Net.Http.Json;
using System.Text;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.SalesChannels;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.RateLimiting;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Wildberries;

public class WildberriesApiKeyValidator : IApiKeyValidator
{
    private readonly HttpClient _httpClient;
    private readonly IChannelRateLimiter _rateLimiter;

    public WildberriesApiKeyValidator(HttpClient httpClient, IChannelRateLimiter rateLimiter)
    {
        _httpClient = httpClient;
        _rateLimiter = rateLimiter;
    }

    public async Task<ApiKeyValidationResult> ValidateAsync(
        string apiKey,
        string? apiSecret,
        Dictionary<string, string>? settings,
        CancellationToken ct)
    {
        // The validator hits the same content endpoint the products import uses, so it paces on
        // that method's bucket: an extra untracked call here would spend the shared per-key
        // allowance without the limiter ever learning about it.
        RateLimitScope scope = new RateLimitScope(
            ChannelCatalog.WildberriesCode,
            apiKey,
            WildberriesApiMethodRegistry.ContentCardsList);

        RateLimitAcquireResult acquireResult = await _rateLimiter.TryAcquireAsync(scope, ct);

        if (!acquireResult.Acquired)
        {
            return ApiKeyValidationResult.Failure(
                "Валидация отложена: лимит запросов Wildberries ещё не освободил следующий слот. " +
                "Повторите попытку через несколько минут.");
        }

        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://content-api.wildberries.ru/content/v2/get/cards/list");

        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        string body = "{ \"settings\": { \"sort\": { \"ascending\": true }, \"cursor\": { \"limit\": 1 }, \"filter\": { \"withPhoto\": -1 } } }";
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(request, ct);

            RateLimitSignal signal = WildberriesRateLimitHeaders.Parse(response);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                await _rateLimiter.ReportSuccessAsync(scope, signal, ct);

                return ApiKeyValidationResult.Success();
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await _rateLimiter.ReportRateLimitedAsync(scope, signal, ct);

                return ApiKeyValidationResult.Failure(
                    "Wildberries временно ограничил запросы. Попробуйте позже.");
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
