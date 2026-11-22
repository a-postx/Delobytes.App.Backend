using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Domain.Entities;

namespace Delobytes.App.Backend.Finance.Application.Interfaces;

/// <summary>
/// Persistence port of the per-channel feed checkpoints.
/// </summary>
public interface IPnlFeedStateStore
{
    /// <summary>
    /// Loads the checkpoint of the channel, or null when the channel has never delivered a batch.
    /// </summary>
    /// <param name="channelId">Channel identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The checkpoint found.</returns>
    public Task<PnlFeedState?> LoadAsync(Guid channelId, CancellationToken cancellationToken);

    /// <summary>
    /// Loads every checkpoint of the tenant.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The checkpoints of the tenant.</returns>
    public Task<IReadOnlyList<PnlFeedState>> LoadAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Records the outcome of one batch, creating the channel checkpoint on the first delivery.
    /// A batch older than the recorded one is ignored, so a redelivered batch never moves the
    /// checkpoint backwards.
    /// </summary>
    /// <param name="checkpoint">Batch outcome.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RecordBatchAsync(PnlFeedCheckpoint checkpoint, CancellationToken cancellationToken);
}
