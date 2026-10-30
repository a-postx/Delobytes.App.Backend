using System.Security.Cryptography;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Splits one fetched unit of orders into batches, publishes each as an
/// <see cref="OrdersBatchImportRequestedEvent"/>, and persists the progress checkpoint.
/// </summary>
internal sealed class OrdersBatchPublisher
{
    public OrdersBatchPublisher(
        IEventPublisher eventPublisher,
        OrdersImportCheckpointStore checkpointStore)
    {
        _eventPublisher = eventPublisher;
        _checkpointStore = checkpointStore;
    }

    private readonly IEventPublisher _eventPublisher;
    private readonly OrdersImportCheckpointStore _checkpointStore;

    /// <summary>
    /// Publishes one unit of work (one backfill day, or the whole delta window) as soon as it is
    /// fetched, then persists how many batches have been published so far so a crash immediately
    /// afterwards cannot lose more than the next unit.
    /// </summary>
    public async Task PublishUnitAsync(
        JobRun jobRun,
        Connection connection,
        OrdersImportBatchCursor cursor,
        IReadOnlyList<ChannelOrderSnapshot> unitOrders,
        CancellationToken cancellationToken)
    {
        if (unitOrders.Count == 0)
        {
            return;
        }

        foreach (List<ChannelOrderSnapshot> batch in SplitIntoBatches(unitOrders))
        {
            OrdersBatchImportRequestedEvent batchEvent = new OrdersBatchImportRequestedEvent
            {
                JobRunId = jobRun.Id,
                ConnectionId = connection.Id,
                ChannelId = connection.ChannelId,
                BatchId = CreateBatchId(jobRun.Id, cursor.TakeNext()),
                Orders = batch,
                IsTerminal = false,
            };

            await _eventPublisher.PublishAsync(batchEvent, cancellationToken);
        }

        OrdersImportCheckpointPayload progressPayload = new OrdersImportCheckpointPayload
        {
            TotalBatches = cursor.NextIndex,
            WindowTo = cursor.WindowTo,
            IsBackfill = cursor.IsBackfill,
            IsFetchCompleted = false,
        };

        await _checkpointStore.SaveAsync(jobRun.Id, progressPayload, cancellationToken);
    }

    /// <summary>
    /// Publishes the empty terminal batch. Does not write a checkpoint: the final checkpoint write
    /// stays with the caller, because it must happen before the terminal publish and carries
    /// MaxChangedAt, PagesFetched and HandoverWatermark.
    /// </summary>
    public async Task PublishTerminalAsync(
        JobRun jobRun,
        Connection connection,
        int batchIndex,
        CancellationToken cancellationToken)
    {
        OrdersBatchImportRequestedEvent terminalBatchEvent = new OrdersBatchImportRequestedEvent
        {
            JobRunId = jobRun.Id,
            ConnectionId = connection.Id,
            ChannelId = connection.ChannelId,
            BatchId = CreateBatchId(jobRun.Id, batchIndex),
            Orders = new List<ChannelOrderSnapshot>(),
            IsTerminal = true,
        };

        await _eventPublisher.PublishAsync(terminalBatchEvent, cancellationToken);
    }

    /// <summary>
    /// Deterministic per (JobRunId, index) so a redelivered orchestrator republishes the same
    /// BatchIds and the aggregator deduplicates them instead of double counting.
    /// </summary>
    internal static Guid CreateBatchId(Guid jobRunId, int batchIndex)
    {
        byte[] input = new byte[20];
        jobRunId.ToByteArray().CopyTo(input, 0);
        BitConverter.GetBytes(batchIndex).CopyTo(input, 16);
        byte[] hash = MD5.HashData(input);
        return new Guid(hash);
    }

    internal static List<List<ChannelOrderSnapshot>> SplitIntoBatches(IReadOnlyList<ChannelOrderSnapshot> orders)
    {
        List<List<ChannelOrderSnapshot>> batches = new List<List<ChannelOrderSnapshot>>();

        for (int i = 0; i < orders.Count; i += ProcessOrdersImportConsumer.BatchSize)
        {
            batches.Add(orders.Skip(i).Take(ProcessOrdersImportConsumer.BatchSize).ToList());
        }

        // An empty terminal batch still flows through Sales so the aggregator finalises the run.
        if (batches.Count == 0)
        {
            batches.Add(new List<ChannelOrderSnapshot>());
        }

        return batches;
    }
}
