using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// DelegatingHandler that persists a verbatim copy of every marketplace API request/response
/// pair for audit purposes (table RawApiResponses).
/// </summary>
/// <remarks>
/// Positioned between the channel's auth handler and the retry/resilience handler, so it
/// observes exactly one row per real HTTP attempt: resilience-driven retries each produce their
/// own HttpRequestMessage and therefore their own captured row, while a single logical API call
/// that succeeds on the first attempt produces exactly one.
///
/// Message handlers added via <c>AddHttpMessageHandler</c> are built from a dedicated
/// HttpClientFactory handler-lifetime DI scope, not from the scope of the code that issued the
/// HTTP call.
/// </remarks>
public class RawApiResponseCaptureHandler : DelegatingHandler
{
    private const int MaxEndpointLength = 500;

    private readonly ISyncJobExecutionContext _syncJobExecutionContext;
    private readonly IRawApiResponseRepository _rawApiResponseRepository;
    private readonly ILogger<RawApiResponseCaptureHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RawApiResponseCaptureHandler"/> class.
    /// </summary>
    public RawApiResponseCaptureHandler(
        ISyncJobExecutionContext syncJobExecutionContext,
        IRawApiResponseRepository rawApiResponseRepository,
        ILogger<RawApiResponseCaptureHandler> logger)
    {
        _syncJobExecutionContext = syncJobExecutionContext;
        _rawApiResponseRepository = rawApiResponseRepository;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // RawApiResponse.SyncJobId is a required FK. Calls made outside SyncJob processing
        // (e.g. seller-info lookup during connection setup) have nothing valid to attribute the
        // audit row to, so they pass through uncaptured rather than violating the constraint.
        Guid? syncJobId = _syncJobExecutionContext.SyncJobId;

        if (syncJobId is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        string requestBody = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        string endpoint = GetEndpoint(request);
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;

        try
        {
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

            // Buffers the content into memory: this read does not consume the stream the API
            // client still needs to deserialize afterwards (HttpContent replays buffered reads).
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            await CaptureAsync(
                syncJobId.Value,
                endpoint,
                requestBody,
                responseBody,
                (int)response.StatusCode,
                startedAt,
                cancellationToken);

            return response;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            // Network-level failures (including retries exhausted by the resilience handler
            // further down the pipeline) never reach the API client as an HttpResponseMessage,
            // so this is the only place able to record that the call was attempted at all.
            await CaptureAsync(
                syncJobId.Value,
                endpoint,
                requestBody,
                $"{ex.GetType().Name}: {ex.Message}",
                httpStatusCode: 0,
                startedAt,
                cancellationToken);

            throw;
        }
    }

    private static string GetEndpoint(HttpRequestMessage request)
    {
        string endpoint = request.RequestUri?.PathAndQuery ?? request.RequestUri?.ToString() ?? string.Empty;

        return endpoint.Length > MaxEndpointLength
            ? endpoint[..MaxEndpointLength]
            : endpoint;
    }

    private async Task CaptureAsync(
        Guid syncJobId,
        string endpoint,
        string requestBody,
        string responseBody,
        int httpStatusCode,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        RawApiResponse rawResponse = new RawApiResponse
        {
            Id = Guid.NewGuid(),
            SyncJobId = syncJobId,
            Endpoint = endpoint,
            RequestPayload = requestBody,
            ResponsePayload = responseBody,
            HttpStatusCode = httpStatusCode,
            ReceivedAt = receivedAt,
        };

        try
        {
            _rawApiResponseRepository.Add(rawResponse);
            await _rawApiResponseRepository.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Audit logging must never break the API call it is observing.
            _logger.LogError(ex, "Failed to persist raw API response for SyncJob {SyncJobId}", syncJobId);
        }
    }
}
