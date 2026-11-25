using System.Globalization;
using System.Net;
using System.Text.Json;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;

/// <summary>
/// Ozon daily accruals. The endpoint is per-day, so the walk moves day by day through the requested
/// window and the cursor carries the day it belongs to.
/// </summary>
internal sealed class OzonFinanceClient
{
    private const string AccrualsPath = "/v1/finance/accrual/by-day";
    private const string AccrualsMethod = "finance/accrual/by-day";

    // A finance window is walked day by day, so a routine pacing wait between two days is absorbed
    // in-process; only a longer refusal (a 429 penalty) becomes a deferred page.
    private static readonly TimeSpan SlotWaitTolerance = TimeSpan.FromSeconds(30);

    private readonly OzonRequestPipeline _pipeline;
    private readonly ILogger _logger;

    public OzonFinanceClient(OzonRequestPipeline pipeline, ILogger logger)
    {
        _pipeline = pipeline;
        _logger = logger;
    }

    /// <inheritdoc cref="IFinanceEntriesSource.GetFinanceEntriesAsync"/>
    public async Task<FinanceEntriesPage> GetFinanceEntriesAsync(
        Guid connectionId,
        FinanceEntriesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The accrual endpoint is per-day: `date` names one day and `last_id` continues that day. The
        // day therefore has to travel in the cursor, or a multi-day window would import only its first
        // day while the orchestrator marks the whole window read and moves the watermark past every
        // day in between.
        OzonFinanceCursor position = OzonFinanceCursor.Parse(request.Cursor, request.From);

        DateOnly day = position.Day;
        string cursor = position.LastId;

        if (day > request.To)
        {
            return new FinanceEntriesPage { IsComplete = true };
        }

        object body = new
        {
            date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            last_id = cursor,
        };

        OzonSendResult<OzonAccrualsResponse> result = await _pipeline.SendAsync(
            connectionId,
            new OzonRequest(
                AccrualsPath,
                AccrualsMethod,
                JsonSerializer.Serialize(body, OzonJson.Options),
                SlotWaitTolerance),
            payload => JsonSerializer.Deserialize<OzonAccrualsResponse>(payload, OzonJson.Options),
            cancellationToken);

        // A routine pacing wait is absorbed by the pipeline; only a refusal beyond the tolerance
        // becomes a deferred page, otherwise a six-month backfill would turn into one deferral per day.
        if (result.SlotRefused || result.Throttled)
        {
            return new FinanceEntriesPage
            {
                Throttled = true,
                RetryAt = result.RetryAt,
            };
        }

        if (result.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new UnauthorizedAccessException(
                $"Ozon finance API returned {(int)result.StatusCode}. The API key role must allow the finance method.");
        }

        if (!result.IsSuccess)
        {
            throw new HttpRequestException(
                $"Ozon finance API returned {(int)result.StatusCode}.",
                null,
                result.StatusCode);
        }

        List<OzonAccrual> accruals = result.Body?.Accruals ?? new List<OzonAccrual>();

        OzonFinanceMappingResult mappingResult = OzonFinanceMapper.Map(accruals);

        string? nextLastId = string.IsNullOrEmpty(result.Body?.LastId) ? null : result.Body.LastId;

        if (nextLastId != null && string.Equals(nextLastId, cursor, StringComparison.Ordinal))
        {
            // Guards against an endless loop if the beta ever repeats a cursor. The page is already
            // read, so it is returned and the day is treated as finished.
            _logger.LogWarning(
                "Ozon finance accruals repeated last_id {LastId}; stopping the day loop.", nextLastId);

            nextLastId = null;
        }

        if (nextLastId != null && accruals.Count > 0)
        {
            return new FinanceEntriesPage
            {
                Entries = mappingResult.Entries,
                NextCursor = new OzonFinanceCursor(day, nextLastId).ToString(),
                Diagnostics = mappingResult.Diagnostics,
                IsComplete = false,
            };
        }

        // The day is exhausted: its pages ended, or it carries no accruals at all. Continue with the
        // next day of the window, or finish when this one was the last.
        DateOnly nextDay = day.AddDays(1);
        bool windowCompleted = nextDay > request.To;

        return new FinanceEntriesPage
        {
            Entries = mappingResult.Entries,
            NextCursor = windowCompleted
                ? null
                : new OzonFinanceCursor(nextDay, string.Empty).ToString(),
            Diagnostics = mappingResult.Diagnostics,
            IsComplete = windowCompleted,
        };
    }
}
