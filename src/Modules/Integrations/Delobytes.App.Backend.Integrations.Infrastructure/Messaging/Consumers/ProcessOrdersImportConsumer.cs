using System.Security.Cryptography;
using System.Text.Json;
using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Application.Services.MarketplaceClients;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Orchestrates an orders import run: computes the delta/backfill window from SyncCheckpoint,
/// fetches the whole window (narrowing it while the endpoint reports a partial response), persists
/// the expected batch count in JobCheckpoint and publishes OrdersBatchImportRequestedEvent per
/// batch. Not user-cancellable (scheduled job).
/// </summary>
public class ProcessOrdersImportConsumer
{
    public const int BatchSize = 10;

    /// <summary>
    /// Hard bound on the number of HTTP requests one run may issue. Bisection of a window does
    /// not loop, but the bound protects against a pathological configuration (a floor far below
    /// the requested window) turning one run into an unbounded request storm.
    /// </summary>
    public const int MaxRequestsPerRun = 1000;

    public static readonly TimeSpan SafetyOverlap = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan BackfillWindow = TimeSpan.FromDays(90);

    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    private readonly IJobRunRepository _jobRunRepository;
    private readonly IJobCheckpointRepository _jobCheckpointRepository;
    private readonly ISyncCheckpointRepository _syncCheckpointRepository;
    private readonly IChannelApiClientFactory _clientFactory;
    private readonly IEventPublisher _eventPublisher;
    private readonly JobRunExecutionContext _jobRunExecutionContext;
    private readonly WildberriesImportOptions _options;
    private readonly ILogger<ProcessOrdersImportConsumer> _logger;
    private readonly TimeSpan _minWindow;

    private int _requestsIssued;

    /// <summary>
    /// Rows the source reported as unmappable across every page of the current fetch. Reset per
    /// fetch; non-zero always ends the run in failure, so it exists to make the count visible in
    /// logs and checkpoints rather than to influence the outcome.
    /// </summary>
    private int _rowsWithoutIdentity;

    public ProcessOrdersImportConsumer(
        IJobRunRepository jobRunRepository,
        IJobCheckpointRepository jobCheckpointRepository,
        ISyncCheckpointRepository syncCheckpointRepository,
        IChannelApiClientFactory clientFactory,
        IEventPublisher eventPublisher,
        JobRunExecutionContext jobRunExecutionContext,
        IOptions<WildberriesImportOptions> options,
        ILogger<ProcessOrdersImportConsumer> logger)
    {
        _jobRunRepository = jobRunRepository;
        _jobCheckpointRepository = jobCheckpointRepository;
        _syncCheckpointRepository = syncCheckpointRepository;
        _clientFactory = clientFactory;
        _eventPublisher = eventPublisher;
        _jobRunExecutionContext = jobRunExecutionContext;
        _options = options.Value;

        _minWindow = _options.MinOrdersWindow > TimeSpan.Zero
            ? _options.MinOrdersWindow
            : TimeSpan.FromHours(1);

        _logger = logger;
    }

    public async Task ProcessAsync(OrdersImportRequestedEvent message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ProcessOrdersImportConsumer: JobRunId={JobRunId}, ConnectionId={ConnectionId}",
            message.JobRunId,
            message.ConnectionId);

        JobRun? jobRun = await _jobRunRepository.GetByIdWithConnectionAsync(message.JobRunId, cancellationToken);

        if (jobRun == null)
        {
            _logger.LogWarning("JobRun {JobRunId} not found, skipping", message.JobRunId);
            return;
        }

        if (jobRun.Status != JobStatus.Pending && jobRun.Status != JobStatus.Running)
        {
            _logger.LogWarning(
                "JobRun {JobRunId} is in terminal status {Status}, skipping",
                jobRun.Id,
                jobRun.Status);
            return;
        }

        Connection? connection = jobRun.Connection;

        if (connection == null || !connection.IsActive)
        {
            string reason = connection == null ? "Connection not found" : "Connection is inactive";
            _logger.LogError("JobRun {JobRunId}: {Reason}. Marking as Failed.", jobRun.Id, reason);
            await FailJobAsync(jobRun, reason);
            return;
        }

        OrdersImportJob? ordersImportJob = await _jobRunRepository.GetOrdersImportJobAsync(jobRun.Id, cancellationToken);

        if (ordersImportJob == null)
        {
            _logger.LogWarning("JobRun {JobRunId}: OrdersImportJob row is missing, continuing", jobRun.Id);
        }

        if (jobRun.Status == JobStatus.Pending)
        {
            jobRun.Status = JobStatus.Running;
            jobRun.StartedAt = DateTimeOffset.UtcNow;
            await _jobRunRepository.UpdateAsync(jobRun, cancellationToken);
        }

        // Correlates every HTTP call made through apiClient below with this JobRun, so
        // RawApiResponseCaptureHandler can attribute captured rows to it. The finally below covers
        // every exit path of the try, including the early returns inside it.
        _jobRunExecutionContext.SetCurrent(jobRun.Id);

        try
        {
            IChannelApiClient apiClient = _clientFactory.Create(connection.SystemChannelTemplate);

            if (apiClient is not IOrdersSource ordersSource)
            {
                string error = $"Channel '{connection.SystemChannelTemplate.Code}' does not support orders import";
                _logger.LogError("JobRun {JobRunId}: {Error}", jobRun.Id, error);
                await FailJobAsync(jobRun, error);
                return;
            }

            JobCheckpoint? existingJobCheckpoint = await _jobCheckpointRepository
                .GetByJobRunIdAsync(jobRun.Id, cancellationToken);
            OrdersImportCheckpointPayload? previousPayload = DeserializePayload(existingJobCheckpoint?.Payload);

            SyncCheckpoint? syncCheckpoint = await _syncCheckpointRepository
                .GetAsync(connection.Id, JobType.OrdersImport, cancellationToken);

            // A redelivered run must re-read the same window, otherwise the batch count persisted
            // for the aggregator would no longer match what is being republished.
            DateTimeOffset windowTo = previousPayload?.WindowTo ?? DateTimeOffset.UtcNow;
            DateTimeOffset windowFrom;
            int flag;

            if (syncCheckpoint?.LastChangedAt != null)
            {
                windowFrom = syncCheckpoint.LastChangedAt.Value - SafetyOverlap;
                flag = 0;
            }
            else
            {
                windowFrom = windowTo - BackfillWindow;
                flag = 1;
            }

            _logger.LogInformation(
                "JobRun {JobRunId}: fetching orders from {From} to {To} (flag={Flag})",
                jobRun.Id,
                windowFrom,
                windowTo,
                flag);

            OrdersRequest request = new OrdersRequest
            {
                DateFrom = windowFrom.ToOffset(MoscowOffset),
                DateTo = windowTo.ToOffset(MoscowOffset),
                Flag = flag,
                Limit = 0,
            };

            OrdersFetchResult fetchResult = await FetchAllPagesAsync(
                ordersSource, connection.Id, request, jobRun.Id, cancellationToken);

            List<ChannelOrderSnapshot> allOrders = fetchResult.Orders;
            List<List<ChannelOrderSnapshot>> batches = SplitIntoBatches(allOrders);

            OrdersImportCheckpointPayload payload = new OrdersImportCheckpointPayload
            {
                TotalBatches = batches.Count,
                MaxChangedAt = GetMaxChangedAt(allOrders, windowTo),
                WindowTo = windowTo,
                PagesFetched = fetchResult.RequestsIssued,
            };

            // Must be persisted before the first publish: the aggregator can only finalise once it
            // knows how many batch results to wait for.
            await _jobCheckpointRepository.UpsertAsync(
                new JobCheckpoint
                {
                    JobRunId = jobRun.Id,
                    Payload = JsonSerializer.Serialize(payload),
                    UpdatedAt = DateTimeOffset.UtcNow,
                },
                cancellationToken);

            for (int i = 0; i < batches.Count; i++)
            {
                OrdersBatchImportRequestedEvent batchEvent = new OrdersBatchImportRequestedEvent
                {
                    JobRunId = jobRun.Id,
                    ConnectionId = connection.Id,
                    ChannelId = connection.ChannelId,
                    BatchId = CreateBatchId(jobRun.Id, i),
                    Orders = batches[i],
                    IsTerminal = i == batches.Count - 1,
                };

                await _eventPublisher.PublishAsync(batchEvent, cancellationToken);
            }

            _logger.LogInformation(
                "JobRun {JobRunId}: fetched {Orders} orders in {Requests} requests, published {Batches} batches, rowsWithoutIdentity={RowsWithoutIdentity}",
                jobRun.Id,
                allOrders.Count,
                fetchResult.RequestsIssued,
                batches.Count,
                fetchResult.RowsWithoutIdentity);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JobRun {JobRunId}: orders import failed", jobRun.Id);
            await FailJobAsync(jobRun, ex.Message);
        }
        finally
        {
            _jobRunExecutionContext.Clear();
        }
    }

    /// <summary>
    /// Deterministic per (JobRunId, index) so a redelivered orchestrator republishes the same
    /// BatchIds and the aggregator deduplicates them instead of double counting.
    /// </summary>
    public static Guid CreateBatchId(Guid jobRunId, int batchIndex)
    {
        byte[] input = new byte[20];
        jobRunId.ToByteArray().CopyTo(input, 0);
        BitConverter.GetBytes(batchIndex).CopyTo(input, 16);
        byte[] hash = MD5.HashData(input);
        return new Guid(hash);
    }

    private async Task<OrdersFetchResult> FetchAllPagesAsync(
        IOrdersSource ordersSource,
        Guid connectionId,
        OrdersRequest request,
        Guid jobRunId,
        CancellationToken cancellationToken)
    {
        Dictionary<string, ChannelOrderSnapshot> ordersByOrderKey = new Dictionary<string, ChannelOrderSnapshot>(StringComparer.Ordinal);

        _requestsIssued = 0;
        _rowsWithoutIdentity = 0;

        await FetchWindowAsync(ordersSource, connectionId, request, jobRunId, ordersByOrderKey, cancellationToken);

        return new OrdersFetchResult
        {
            Orders = ordersByOrderKey.Values.ToList(),
            RequestsIssued = _requestsIssued,
            RowsWithoutIdentity = _rowsWithoutIdentity,
        };
    }

    /// <summary>
    /// Fetches one window. The endpoint serves rows in ascending order of the selection instant and
    /// caps a response at a fixed row count, so a capped response proves that everything up to its
    /// last returned instant was served and leaves only the region above that instant unknown. The
    /// recursion therefore splits that unknown region at its midpoint and resumes the left half from
    /// the covered boundary, not from the requested <c>dateFrom</c>: the endpoint has no
    /// <c>dateTo</c>, so re-requesting the parent's own <c>dateFrom</c> would return the identical
    /// block, report the cap again, and never advance. Rows falling exactly on a boundary are
    /// reported by both siblings; the dedup dictionary collapses them into one order and unions
    /// their lines, so the bisection can cut through a cart without losing or duplicating a unit.
    /// Rows the source could not identify fail the window outright: the dictionary only merges rows
    /// that agree on identity, and an unidentified row has none to agree on.
    /// </summary>
    private async Task FetchWindowAsync(
        IOrdersSource ordersSource,
        Guid connectionId,
        OrdersRequest request,
        Guid jobRunId,
        Dictionary<string, ChannelOrderSnapshot> ordersByOrderKey,
        CancellationToken cancellationToken)
    {
        if (_requestsIssued >= MaxRequestsPerRun)
        {
            throw new InvalidOperationException(
                $"Orders import exceeded {MaxRequestsPerRun} requests for JobRun {jobRunId}.");
        }

        _requestsIssued++;

        OrdersPage ordersPage = await ordersSource.GetOrdersAsync(connectionId, request, cancellationToken);

        if (ordersPage.RowsWithoutIdentity > 0)
        {
            // A page that dropped rows cannot be imported partially: the missing orders would be
            // indistinguishable from orders that never existed, and advancing the watermark past
            // this window would hide them from every later delta run as well. Failing here reuses
            // the same fail-fast stance as the unresolvable partial response below, and lets the
            // next scheduled run retry the window once the source data is complete.
            _rowsWithoutIdentity += ordersPage.RowsWithoutIdentity;

            _logger.LogError(
                "JobRun {JobRunId}: window {From} - {To} returned {RowsWithoutIdentity} row(s) without a usable order identity, failing the run",
                jobRunId,
                request.DateFrom,
                request.DateTo,
                ordersPage.RowsWithoutIdentity);

            throw new InvalidOperationException(
                $"Orders import received {ordersPage.RowsWithoutIdentity} row(s) without a usable order identity " +
                $"(no gNumber, or no srid and no sticker) in window {request.DateFrom:O} - {request.DateTo:O}; " +
                "the window cannot be imported without inventing an identity that may split or merge real orders.");
        }

        foreach (ChannelOrderSnapshot order in ordersPage.Orders)
        {
            // Sibling windows of one bisection share their boundary instant, so an order placed on
            // that instant is legitimately reported by both halves. That is one order observed
            // twice, not two orders colliding, so the reports are merged — never re-keyed.
            if (!ordersByOrderKey.TryGetValue(order.ExternalOrderId, out ChannelOrderSnapshot? existing))
            {
                ordersByOrderKey.Add(order.ExternalOrderId, order);
                continue;
            }

            // "Which report is newer" and "which lines we have" are two independent questions.
            // IsLaterReport below answers only the first, and it decides the fields that are neither
            // derived from lines nor re-derivable after the merge (warehouse, region, substatus).
            // The line union happens regardless of its result, because the newer report frequently
            // carries a subset of the cart. A bisection splits carts, so half A can hold unit 1 and
            // half B unit 2 of one gNumber; letting the winner's Lines replace the accumulated set
            // would silently drop whichever unit the older half contributed.
            bool isIncomingLater = IsLaterReport(order, existing);

            List<ChannelOrderLineSnapshot> mergedLines = UnionLines(existing.Lines, order.Lines);
            ChannelOrderSnapshot headerSource = isIncomingLater ? order : existing;

            // The header is reduced from the merged line set, never taken from the winning report:
            // either half may hold the only cancelled unit of a cart, or the only live one, and the
            // winning report's own header describes the units it happened to carry. The placement
            // instant goes through the same reduction, so the merged cart keeps the earliest instant
            // any half reported.
            ChannelOrderHeaderFacts mergedHeader = ChannelOrderHeaderFacts.Reduce(
                mergedLines,
                new[] { existing.OrderDate, order.OrderDate });

            ordersByOrderKey[order.ExternalOrderId] = headerSource with
            {
                Lines = mergedLines,
                ExternalStatus = mergedHeader.ExternalStatus,
                CancelReason = mergedHeader.CancelReason,
                OrderDate = mergedHeader.OrderDate,
                StatusChangedAt = mergedHeader.StatusChangedAt,
                ChannelReportedTotal = mergedHeader.ChannelReportedTotal
            };
        }

        // The endpoint filters by dateFrom alone and serves rows in ascending order of the selection
        // instant, capped at MaxOrdersPerResponse rows. A capped response therefore proves that
        // everything up to its last returned instant was served, and says nothing about what lies
        // beyond it. Bisecting the requested window instead re-issues the parent's own request: the
        // left half keeps the parent's dateFrom, the endpoint has no dateTo, so it returns the same
        // block, reports the cap again, and recurses down to the window floor without having read a
        // single new row - failing the run on volume alone. The bisection has to start where the
        // unknown region starts, which is the covered boundary.
        // A null CoveredUntil on a capped response means the source could not prove how far it
        // reached; falling back to the requested DateFrom keeps the run honest, since it narrows
        // and retries rather than assuming the window was served.
        DateTimeOffset nextFrom = request.DateFrom;

        if (ordersPage.CoveredUntil != null && ordersPage.CoveredUntil.Value > nextFrom)
        {
            nextFrom = ordersPage.CoveredUntil.Value;
        }

        // Nothing is left unknown: either the response came back whole, or it reached the window end
        // before hitting the cap, in which case the rows past the window are what filled it.
        if (!ordersPage.HasMore || nextFrom >= request.DateTo)
        {
            return;
        }

        TimeSpan unknownSpan = request.DateTo - nextFrom;

        if (unknownSpan <= _minWindow)
        {
            // Accepting the truncated response would silently drop orders, which is the defect this
            // path exists to fix; the run fails instead so the operator sees a window granularity
            // that the endpoint cannot serve rather than a partial import reported as success.
            _logger.LogWarning(
                "JobRun {JobRunId}: window {From} - {To} still returns a partial response with {Remaining} left to read at the minimum granularity of {MinWindow}, failing the run",
                jobRunId,
                nextFrom,
                request.DateTo,
                unknownSpan,
                _minWindow);

            throw new InvalidOperationException(
                $"Orders import cannot narrow window {nextFrom:O} - {request.DateTo:O} below {_minWindow} " +
                "while the endpoint keeps reporting a partial response.");
        }

        DateTimeOffset midpoint = nextFrom + TimeSpan.FromTicks(unknownSpan.Ticks / 2);

        OrdersRequest firstHalf = new OrdersRequest
        {
            DateFrom = nextFrom,
            DateTo = midpoint,
            Flag = request.Flag,
            Limit = request.Limit,
        };

        OrdersRequest secondHalf = new OrdersRequest
        {
            DateFrom = midpoint,
            DateTo = request.DateTo,
            Flag = request.Flag,
            Limit = request.Limit,
        };

        _logger.LogInformation(
            "JobRun {JobRunId}: window {From} - {To} exceeded the response cap, covered until {CoveredUntil}, splitting at {Midpoint}",
            jobRunId,
            request.DateFrom,
            request.DateTo,
            nextFrom,
            midpoint);

        await FetchWindowAsync(ordersSource, connectionId, firstHalf, jobRunId, ordersByOrderKey, cancellationToken);
        await FetchWindowAsync(ordersSource, connectionId, secondHalf, jobRunId, ordersByOrderKey, cancellationToken);
    }

    /// <summary>
    /// Unions the lines of two reports of one order, keyed by
    /// <see cref="ChannelOrderLineSnapshot.LineKey"/>. An unseen key is appended, keeping the lines
    /// already known; a key present on both sides keeps the version whose own
    /// <see cref="ChannelOrderLineSnapshot.StatusChangedAt"/> is later, and keeps the accumulated
    /// version on an exact tie, so the result does not depend on arrival order. The comparison is per
    /// line rather than per report: the order-level instant says nothing about which version of one
    /// unit is fresher, and two reports that split a cart usually do not carry the same unit at all.
    /// Returning a fresh list rather than mutating either argument is what lets
    /// <see cref="ChannelOrderSnapshot"/> stay an immutable record without making Lines settable.
    /// </summary>
    private static List<ChannelOrderLineSnapshot> UnionLines(
        List<ChannelOrderLineSnapshot> existing,
        List<ChannelOrderLineSnapshot> incoming)
    {
        List<ChannelOrderLineSnapshot> merged = new List<ChannelOrderLineSnapshot>(existing);

        foreach (ChannelOrderLineSnapshot line in incoming)
        {
            int index = merged.FindIndex(
                existingLine => string.Equals(existingLine.LineKey, line.LineKey, StringComparison.Ordinal));

            if (index < 0)
            {
                merged.Add(line);
                continue;
            }

            if (IsLaterLine(line, merged[index]))
            {
                merged[index] = line;
            }
        }

        return merged;
    }

    /// <summary>
    /// Orders whether <paramref name="candidate"/> is a later version of one line than
    /// <paramref name="current"/>. A version reporting no instant never displaces one that reports
    /// an instant, and equal instants resolve in favour of the accumulated version, so replaying a
    /// report cannot flip the stored facts back and forth.
    /// </summary>
    private static bool IsLaterLine(ChannelOrderLineSnapshot candidate, ChannelOrderLineSnapshot current)
    {
        DateTimeOffset? candidateInstant = candidate.StatusChangedAt;
        DateTimeOffset? currentInstant = current.StatusChangedAt;

        if (candidateInstant == null)
        {
            return false;
        }

        if (currentInstant == null)
        {
            return true;
        }

        return candidateInstant.Value > currentInstant.Value;
    }

    /// <summary>
    /// Derives the next delta watermark from what the endpoint actually selected on:
    /// <c>lastChangeDate</c> for a flag=0 run. A row that reports no <c>lastChangeDate</c>
    /// (unparseable or absent) contributes nothing, and <c>OrderDate</c> is never substituted —
    /// it lives on a different time axis than the one the endpoint filters by, so folding it in
    /// would push the watermark past the served instant and drop the row from every later delta
    /// run. The result is clamped to the run's own window end, since nothing the run fetched can
    /// justify a later watermark; the checkpointed <paramref name="windowTo"/> makes that clamp
    /// stable across redelivery.
    /// </summary>
    private DateTimeOffset? GetMaxChangedAt(List<ChannelOrderSnapshot> orders, DateTimeOffset windowTo)
    {
        DateTimeOffset? max = null;

        foreach (ChannelOrderSnapshot order in orders)
        {
            if (order.StatusChangedAt == null)
            {
                continue;
            }

            DateTimeOffset value = order.StatusChangedAt.Value;

            if (max == null || value > max.Value)
            {
                max = value;
            }
        }

        if (max.HasValue && max.Value > windowTo)
        {
            _logger.LogWarning(
                "Orders import: observed lastChangeDate {Observed} is later than the run window end {WindowTo}, clamping the watermark",
                max.Value,
                windowTo);

            return windowTo;
        }

        return max?.ToUniversalTime();
    }

    private static List<List<ChannelOrderSnapshot>> SplitIntoBatches(List<ChannelOrderSnapshot> orders)
    {
        List<List<ChannelOrderSnapshot>> batches = new List<List<ChannelOrderSnapshot>>();

        for (int i = 0; i < orders.Count; i += BatchSize)
        {
            batches.Add(orders.Skip(i).Take(BatchSize).ToList());
        }

        // An empty terminal batch still flows through Sales so the aggregator finalises the run.
        if (batches.Count == 0)
        {
            batches.Add(new List<ChannelOrderSnapshot>());
        }

        return batches;
    }

    /// <summary>
    /// Orders whether <paramref name="candidate"/> is a later report of the same order than
    /// <paramref name="current"/>. <c>StatusChangedAt</c> is the channel's own "as of" instant and
    /// decides; <c>OrderDate</c> breaks the tie where a channel reports no change time, and an
    /// exact tie keeps the current report so the merge does not depend on arrival order. Note that
    /// the snapshots are records whose <c>Lines</c> is a list, so value equality would be
    /// reference-based anyway — comparing the instants is both correct and order-independent.
    /// </summary>
    private static bool IsLaterReport(ChannelOrderSnapshot candidate, ChannelOrderSnapshot current)
    {
        DateTimeOffset candidateInstant = candidate.StatusChangedAt ?? candidate.OrderDate;
        DateTimeOffset currentInstant = current.StatusChangedAt ?? current.OrderDate;

        return candidateInstant > currentInstant;
    }

    private static OrdersImportCheckpointPayload? DeserializePayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OrdersImportCheckpointPayload>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task FailJobAsync(JobRun jobRun, string error)
    {
        jobRun.Status = JobStatus.Failed;
        jobRun.Error = error;
        jobRun.CompletedAt = DateTimeOffset.UtcNow;

        // The consumer token may already be cancelled; the failure must still be persisted.
        await _jobRunRepository.UpdateAsync(jobRun, CancellationToken.None);
    }
}
