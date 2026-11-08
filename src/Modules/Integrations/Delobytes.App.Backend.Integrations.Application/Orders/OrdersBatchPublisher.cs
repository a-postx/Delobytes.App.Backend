using System.Security.Cryptography;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Splits one fetched unit of orders into batches, publishes each as an
/// <see cref="OrdersBatchImportRequestedEvent"/>, and persists the progress checkpoint.
/// </summary>
public sealed class OrdersBatchPublisher
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
    internal async Task PublishUnitAsync(
        OrdersImportContext context,
        RunCounters counters,
        BatchSequence batches,
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
                JobRunId = context.JobRun.Id,
                ConnectionId = context.Connection.Id,
                ChannelId = context.Connection.ChannelId,
                BatchId = CreateBatchId(context.JobRun.Id, batches.TakeNext()),
                Orders = batch,
            };

            await _eventPublisher.PublishAsync(batchEvent, cancellationToken);
        }

        await _checkpointStore.SaveProgressAsync(
            context.JobRun.Id, context, counters, batches, cancellationToken);
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

        for (int i = 0; i < orders.Count; i += OrdersImportTuning.DefaultBatchSize)
        {
            batches.Add(orders.Skip(i).Take(OrdersImportTuning.DefaultBatchSize).ToList());
        }

        return batches;
    }
}
