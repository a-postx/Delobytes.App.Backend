using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Finance.Infrastructure.Persistence.Stores;

/// <summary>
/// EF Core implementation of <see cref="IPnlFeedStateStore"/>.
/// </summary>
public class PnlFeedStateStore : IPnlFeedStateStore
{
    private readonly FinanceDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="PnlFeedStateStore"/> class.
    /// </summary>
    /// <param name="context">Finance database context.</param>
    public PnlFeedStateStore(FinanceDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<PnlFeedState?> LoadAsync(Guid channelId, CancellationToken cancellationToken)
    {
        return await _context.PnlFeedStates
            .AsNoTracking()
            .FirstOrDefaultAsync(state => state.ChannelId == channelId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PnlFeedState>> LoadAllAsync(CancellationToken cancellationToken)
    {
        return await _context.PnlFeedStates
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task RecordBatchAsync(PnlFeedCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        PnlFeedState? state = await _context.PnlFeedStates
            .FirstOrDefaultAsync(s => s.ChannelId == checkpoint.ChannelId, cancellationToken);

        if (state == null)
        {
            state = new PnlFeedState
            {
                Id = Guid.NewGuid(),
                ChannelId = checkpoint.ChannelId,
                CreatedAt = checkpoint.BatchCompletedAt,
            };

            _context.PnlFeedStates.Add(state);
        }
        else if (checkpoint.BatchCompletedAt <= state.LastBatchAt)
        {
            // At-least-once delivery: a redelivered or reordered batch carries an older instant.
            // Keeping the newer checkpoint is what stops the freshness heading from going backwards.
            return;
        }
        else
        {
            state.UpdatedAt = checkpoint.BatchCompletedAt;
        }

        state.LastJobRunId = checkpoint.JobRunId;
        state.LastBatchAt = checkpoint.BatchCompletedAt;
        state.LastBatchAppended = checkpoint.Appended;
        state.LastBatchDuplicates = checkpoint.Duplicates;
        state.LastBatchFailed = checkpoint.Failed;

        // A batch that only produced duplicates did arrive, but the figures did not move: the
        // dashboard shows that separately instead of claiming the data changed.
        if (checkpoint.Appended > 0)
        {
            state.LastChangeAt = checkpoint.BatchCompletedAt;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
