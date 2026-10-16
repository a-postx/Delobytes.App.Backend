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
    public static readonly TimeSpan BackfillWindow = TimeSpan.FromDays(30);

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

            JobCheckpoint? existingJobCheckpoint = await _jobCheckpointRepository.GetByJobRunIdAsync(jobRun.Id, cancellationToken);
            OrdersImportCheckpointPayload? previousPayload = DeserializePayload(existingJobCheckpoint?.Payload);

            SyncCheckpoint? syncCheckpoint = await _syncCheckpointRepository.GetAsync(
                connection.Id, JobType.OrdersImport, cancellationToken);

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
                "JobRun {JobRunId}: fetched {Orders} orders in {Requests} requests, published {Batches} batches",
                jobRun.Id,
                allOrders.Count,
                fetchResult.RequestsIssued,
                batches.Count);
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
        Dictionary<string, ChannelOrderSnapshot> ordersByExternalId = new Dictionary<string, ChannelOrderSnapshot>(StringComparer.Ordinal);

        _requestsIssued = 0;

        await FetchWindowAsync(ordersSource, connectionId, request, jobRunId, ordersByExternalId, cancellationToken);

        return new OrdersFetchResult
        {
            Orders = ordersByExternalId.Values.ToList(),
            RequestsIssued = _requestsIssued,
        };
    }

    /// <summary>
    /// Fetches one window. If the endpoint reports the response was a partial subset of the window,
    /// the window is split at its midpoint and both halves are fetched the same way, because the
    /// endpoint exposes no ordering guarantee: the rows it returned cannot be assumed to be the
    /// earliest ones, so advancing <c>dateFrom</c> past them would silently drop the rest of the
    /// window. Rows seen twice (both halves of a split share their boundary instant) are collapsed
    /// by the dedup dictionary.
    /// </summary>
    private async Task FetchWindowAsync(
        IOrdersSource ordersSource,
        Guid connectionId,
        OrdersRequest request,
        Guid jobRunId,
        Dictionary<string, ChannelOrderSnapshot> ordersByExternalId,
        CancellationToken cancellationToken)
    {
        if (_requestsIssued >= MaxRequestsPerRun)
        {
            throw new InvalidOperationException(
                $"Orders import exceeded {MaxRequestsPerRun} requests for JobRun {jobRunId}.");
        }

        _requestsIssued++;

        OrdersPage ordersPage = await ordersSource.GetOrdersAsync(connectionId, request, cancellationToken);

        foreach (ChannelOrderSnapshot order in ordersPage.Orders)
        {
            ordersByExternalId[order.ExternalOrderId] = order;
        }

        if (!ordersPage.HasMore)
        {
            return;
        }

        TimeSpan windowSpan = request.DateTo - request.DateFrom;

        if (windowSpan <= _minWindow)
        {
            // Accepting the truncated response would silently drop orders, which is the defect this
            // path exists to fix; the run fails instead so the operator sees a window granularity
            // that the endpoint cannot serve rather than a partial import reported as success.
            _logger.LogWarning(
                "JobRun {JobRunId}: window {From} - {To} still returns a partial response at the minimum granularity of {MinWindow}, failing the run",
                jobRunId,
                request.DateFrom,
                request.DateTo,
                _minWindow);

            throw new InvalidOperationException(
                $"Orders import cannot narrow window {request.DateFrom:O} - {request.DateTo:O} below {_minWindow} " +
                "while the endpoint keeps reporting a partial response.");
        }

        DateTimeOffset midpoint = request.DateFrom + TimeSpan.FromTicks(windowSpan.Ticks / 2);

        OrdersRequest firstHalf = new OrdersRequest
        {
            DateFrom = request.DateFrom,
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
            "JobRun {JobRunId}: window {From} - {To} exceeded the response cap, splitting at {Midpoint}",
            jobRunId,
            request.DateFrom,
            request.DateTo,
            midpoint);

        await FetchWindowAsync(ordersSource, connectionId, firstHalf, jobRunId, ordersByExternalId, cancellationToken);
        await FetchWindowAsync(ordersSource, connectionId, secondHalf, jobRunId, ordersByExternalId, cancellationToken);
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
