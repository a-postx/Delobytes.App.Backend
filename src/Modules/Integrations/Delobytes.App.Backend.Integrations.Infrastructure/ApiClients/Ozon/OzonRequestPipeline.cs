using System.Net;
using System.Text;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.SalesChannels;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;

/// <summary>
/// The single implementation of an Ozon HTTP call: connection, limiter admission, POST, rate-limit
/// feedback and status classification. Callers own only the request body and the page they build from
/// the answer.
/// </summary>
internal sealed class OzonRequestPipeline
{
    private const int ResponseBodyLogLimit = 500;

    private readonly HttpClient _httpClient;
    private readonly IConnectionResolver _connectionResolver;
    private readonly IChannelRateLimiter _rateLimiter;
    private readonly RateLimitRequestPacer _pacer;
    private readonly ILogger _logger;
    private readonly OzonEndpoint _endpoint;

    public OzonRequestPipeline(
        HttpClient httpClient,
        IConnectionResolver connectionResolver,
        IChannelRateLimiter rateLimiter,
        ILogger logger,
        TimeProvider timeProvider,
        OzonEndpoint endpoint)
    {
        _httpClient = httpClient;
        _connectionResolver = connectionResolver;
        _rateLimiter = rateLimiter;
        _logger = logger;
        _endpoint = endpoint;
        _pacer = new RateLimitRequestPacer(rateLimiter, timeProvider, logger);
    }

    /// <summary>
    /// Issues one request. A limiter refusal and a <c>429</c> both return without throwing, because the
    /// caller has to keep its cursor and retry later; a non-success status other than <c>429</c> is
    /// also returned, because what it means (a deferred page, a key-role problem, a failed run)
    /// differs per capability.
    /// </summary>
    /// <typeparam name="T">Shape the response body is read into.</typeparam>
    public async Task<OzonSendResult<T>> SendAsync<T>(
        Guid connectionId,
        OzonRequest request,
        Func<string, T?> readBody,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(readBody);

        Connection connection = await _connectionResolver.ResolveByIdAsync(connectionId, cancellationToken);

        RateLimitScope scope = new RateLimitScope(ChannelCatalog.OzonCode, connection.ApiKey, request.RateLimiterMethod);

        if (request.SlotWaitTolerance is { } tolerance)
        {
            RateLimitAcquireResult? refusedSlot = await _pacer.WaitForSlotAsync(scope, tolerance, cancellationToken);

            if (refusedSlot is not null)
            {
                // Not a failure and not an empty page: the caller keeps its cursor and returns later.
                _logger.LogInformation(
                    "Ozon request for connection {ConnectionId} was not issued: the rate limiter has not " +
                    "released the next slot for method {Method} before {NextAllowed:O}.",
                    connectionId,
                    scope.Method,
                    refusedSlot.NextRequestAllowedAt);

                return OzonSendResult<T>.Refused(refusedSlot.NextRequestAllowedAt);
            }
        }
        else
        {
            RateLimitAcquireResult acquireResult = await _rateLimiter.TryAcquireAsync(scope, cancellationToken);

            if (!acquireResult.Acquired)
            {
                _logger.LogInformation(
                    "Ozon request for connection {ConnectionId} was not issued: the rate limiter has not " +
                    "released the next slot for method {Method} yet.",
                    connectionId,
                    scope.Method);

                return OzonSendResult<T>.Refused(acquireResult.NextRequestAllowedAt);
            }
        }

        using HttpRequestMessage httpRequest = new HttpRequestMessage(
            HttpMethod.Post, $"{_endpoint.RequireBaseUrl()}{request.Path}")
        {
            Content = new StringContent(request.Body, Encoding.UTF8, "application/json"),
        };

        httpRequest.Options.Set(OzonAuthHandler.ConnectionIdOptionKey, connectionId);

        using HttpResponseMessage response = await _httpClient.SendAsync(httpRequest, cancellationToken);

        RateLimitSignal rateLimitSignal = OzonRateLimitHeaders.Parse(response);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            DateTimeOffset retryAt = await _rateLimiter.ReportRateLimitedAsync(
                scope, rateLimitSignal, cancellationToken);

            string throttledBody = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogWarning(
                "Ozon API throttled method {Method} (429): connection {ConnectionId}, x-request-id {RequestId}, " +
                "Ratelimit-Remaining {Remaining}, Retry-After {RetrySeconds}s. Response: {Response}",
                scope.Method,
                connectionId,
                rateLimitSignal.RequestId,
                rateLimitSignal.Remaining,
                rateLimitSignal.RetrySeconds,
                TruncateForLog(throttledBody));

            return OzonSendResult<T>.RateLimited(response.StatusCode, retryAt);
        }

        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogWarning(
                "Ozon API returned {StatusCode} for method {Method} (x-request-id {RequestId}). Response: {Response}",
                (int)response.StatusCode,
                scope.Method,
                rateLimitSignal.RequestId,
                TruncateForLog(errorBody));

            return OzonSendResult<T>.Failed(response.StatusCode);
        }

        // Reported before the body is read: the counters describe this response, and a body that fails
        // to deserialize must not cost the limiter its only feedback on the real allowance. A 5xx never
        // reaches here, so a short outage cannot teach the pacing the wrong allowance.
        await _rateLimiter.ReportSuccessAsync(scope, rateLimitSignal, cancellationToken);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        return OzonSendResult<T>.Ok(readBody(body));
    }

    private static string TruncateForLog(string responseBody)
    {
        if (string.IsNullOrEmpty(responseBody) || responseBody.Length <= ResponseBodyLogLimit)
        {
            return responseBody;
        }

        return responseBody[..ResponseBodyLogLimit];
    }
}
