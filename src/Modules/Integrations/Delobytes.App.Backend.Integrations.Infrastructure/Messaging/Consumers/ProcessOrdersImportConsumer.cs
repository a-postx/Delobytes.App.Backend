using System.Security.Cryptography;
using System.Text.Json;
using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Services.MarketplaceClients;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Orchestrates an orders import run: computes the delta/backfill window from SyncCheckpoint,
/// fetches every page, persists the expected batch count in JobCheckpoint and publishes
/// OrdersBatchImportRequestedEvent per batch. Not user-cancellable (scheduled job).
/// </summary>
public class ProcessOrdersImportConsumer
{
    public const int BatchSize = 10;
    public const int PageLimit = 100;
    public const int MaxPages = 1000;

    public static readonly TimeSpan SafetyOverlap = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan BackfillWindow = TimeSpan.FromDays(30);

    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    private readonly IJobRunRepository _jobRunRepository;
    private readonly IJobCheckpointRepository _jobCheckpointRepository;
    private readonly ISyncCheckpointRepository _syncCheckpointRepository;
    private readonly IChannelApiClientFactory _clientFactory;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ProcessOrdersImportConsumer> _logger;

    public ProcessOrdersImportConsumer(
        IJobRunRepository jobRunRepository,
        IJobCheckpointRepository jobCheckpointRepository,
        ISyncCheckpointRepository syncCheckpointRepository,
        IChannelApiClientFactory clientFactory,
        IEventPublisher eventPublisher,
        ILogger<ProcessOrdersImportConsumer> logger)
    {
        _jobRunRepository = jobRunRepository;
        _jobCheckpointRepository = jobCheckpointRepository;
        _syncCheckpointRepository = syncCheckpointRepository;
        _clientFactory = clientFactory;
        _eventPublisher = eventPublisher;
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
                Limit = PageLimit,
            };

            List<ChannelOrderSnapshot> allOrders = await FetchAllPagesAsync(
                ordersSource, connection.Id, request, jobRun.Id, cancellationToken);

            List<List<ChannelOrderSnapshot>> batches = SplitIntoBatches(allOrders);

            OrdersImportCheckpointPayload payload = new OrdersImportCheckpointPayload
            {
                TotalBatches = batches.Count,
                MaxChangedAt = GetMaxChangedAt(allOrders),
                WindowTo = windowTo,
                PagesFetched = (allOrders.Count / PageLimit) + 1,
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
                    BatchId = CreateBatchId(jobRun.Id, i),
                    Orders = batches[i],
                    IsTerminal = i == batches.Count - 1,
                };

                await _eventPublisher.PublishAsync(batchEvent, cancellationToken);
            }

            _logger.LogInformation(
                "JobRun {JobRunId}: fetched {Orders} orders, published {Batches} batches",
                jobRun.Id,
                allOrders.Count,
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

    private async Task<List<ChannelOrderSnapshot>> FetchAllPagesAsync(
        IOrdersSource ordersSource,
        Guid connectionId,
        OrdersRequest request,
        Guid jobRunId,
        CancellationToken cancellationToken)
    {
        List<ChannelOrderSnapshot> allOrders = new List<ChannelOrderSnapshot>();
        HashSet<string> seenOrderIds = new HashSet<string>(StringComparer.Ordinal);
        OrdersRequest currentRequest = request;

        for (int page = 0; page < MaxPages; page++)
        {
            OrdersPage ordersPage = await ordersSource.GetOrdersAsync(connectionId, currentRequest, cancellationToken);

            foreach (ChannelOrderSnapshot order in ordersPage.Orders)
            {
                if (seenOrderIds.Add(order.ExternalOrderId))
                {
                    allOrders.Add(order);
                }
            }

            if (!ordersPage.HasMore || ordersPage.Orders.Count == 0)
            {
                return allOrders;
            }

            DateTimeOffset? nextFrom = GetPageCursor(ordersPage.Orders, currentRequest.Flag);

            // Without forward progress the same page would be requested forever.
            if (nextFrom == null || nextFrom.Value <= currentRequest.DateFrom)
            {
                _logger.LogWarning(
                    "JobRun {JobRunId}: source reported more orders but the window cannot advance past {From}, stopping",
                    jobRunId,
                    currentRequest.DateFrom);
                return allOrders;
            }

            currentRequest = new OrdersRequest
            {
                DateFrom = nextFrom.Value,
                DateTo = currentRequest.DateTo,
                Flag = currentRequest.Flag,
                Limit = currentRequest.Limit,
            };
        }

        throw new InvalidOperationException($"Orders import exceeded {MaxPages} pages.");
    }

    private static DateTimeOffset? GetPageCursor(List<ChannelOrderSnapshot> orders, int flag)
    {
        DateTimeOffset? max = null;

        foreach (ChannelOrderSnapshot order in orders)
        {
            DateTimeOffset value = flag == 0 ? order.StatusChangedAt ?? order.OrderDate : order.OrderDate;

            if (max == null || value > max.Value)
            {
                max = value;
            }
        }

        return max;
    }

    private static DateTimeOffset? GetMaxChangedAt(List<ChannelOrderSnapshot> orders)
    {
        DateTimeOffset? max = null;

        foreach (ChannelOrderSnapshot order in orders)
        {
            DateTimeOffset value = order.StatusChangedAt ?? order.OrderDate;

            if (max == null || value > max.Value)
            {
                max = value;
            }
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
