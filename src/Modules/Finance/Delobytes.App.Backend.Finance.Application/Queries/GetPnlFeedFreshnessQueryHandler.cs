using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>Freshness of one channel feed.</summary>
public sealed class PnlFeedChannelFreshnessView
{
    /// <summary>Gets or sets the channel identifier.</summary>
    public Guid ChannelId { get; set; }

    /// <summary>Gets or sets the run that delivered the last batch.</summary>
    public Guid LastJobRunId { get; set; }

    /// <summary>Gets or sets the instant the last batch finished being processed.</summary>
    public DateTimeOffset LastBatchAt { get; set; }

    /// <summary>
    /// Gets or sets the instant the data last changed, or null when no batch of this channel has
    /// ever added anything: a channel re-synced without changes keeps the old value here while
    /// <see cref="LastBatchAt"/> moves.
    /// </summary>
    public DateTimeOffset? LastChangeAt { get; set; }

    /// <summary>Gets or sets the number of entries the last batch appended.</summary>
    public int LastBatchAppended { get; set; }

    /// <summary>Gets or sets the number of entries the last batch skipped as duplicates.</summary>
    public int LastBatchDuplicates { get; set; }

    /// <summary>Gets or sets the number of entries of the last batch that failed.</summary>
    public int LastBatchFailed { get; set; }
}

/// <summary>Response of the feed freshness query.</summary>
public sealed class PnlFeedFreshnessResponse
{
    /// <summary>
    /// Gets or sets the server instant the answer was built at, so an age is never computed from the
    /// browser clock.
    /// </summary>
    public DateTimeOffset AsOf { get; set; }

    /// <summary>
    /// Gets or sets the channels, stalest first. A channel missing from this list has never
    /// delivered a batch.
    /// </summary>
    public List<PnlFeedChannelFreshnessView> Channels { get; set; } = new();
}

/// <summary>Per-channel delivery checkpoints of the finance feed.</summary>
public sealed class GetPnlFeedFreshnessQuery : IRequest<PnlFeedFreshnessResponse>
{
}

/// <summary>
/// Handles <see cref="GetPnlFeedFreshnessQuery"/>: reads the checkpoints and orders them so a client
/// shows the least recently refreshed channel first without sorting anything itself.
/// </summary>
public sealed class GetPnlFeedFreshnessQueryHandler : IRequestHandler<GetPnlFeedFreshnessQuery, PnlFeedFreshnessResponse>
{
    private readonly IPnlFeedStateStore _store;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetPnlFeedFreshnessQueryHandler"/> class.
    /// </summary>
    /// <param name="store">Feed checkpoint store.</param>
    /// <param name="timeProvider">Clock source, used for the answer instant.</param>
    public GetPnlFeedFreshnessQueryHandler(IPnlFeedStateStore store, TimeProvider timeProvider)
    {
        _store = store;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task<PnlFeedFreshnessResponse> Handle(
        GetPnlFeedFreshnessQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PnlFeedState> states = await _store.LoadAllAsync(cancellationToken);

        return new PnlFeedFreshnessResponse
        {
            AsOf = _timeProvider.GetUtcNow(),
            Channels = states
                .OrderBy(state => state.LastBatchAt)
                .Select(state => new PnlFeedChannelFreshnessView
                {
                    ChannelId = state.ChannelId,
                    LastJobRunId = state.LastJobRunId,
                    LastBatchAt = state.LastBatchAt,
                    LastChangeAt = state.LastChangeAt,
                    LastBatchAppended = state.LastBatchAppended,
                    LastBatchDuplicates = state.LastBatchDuplicates,
                    LastBatchFailed = state.LastBatchFailed,
                })
                .ToList(),
        };
    }
}
