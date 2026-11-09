using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Catalog.Application.Services;

/// <summary>
/// Deactivates the links of a channel that the finished import run never observed.
/// </summary>
/// <remarks>
/// Runs outside any batch, after Integrations has recorded every batch result of the run — which
/// happens strictly after each batch committed. That ordering is what makes the comparison below
/// correct; performed inside a batch transaction it read the siblings' uncommitted rows as dropped
/// cards and deactivated live links. The sweep is idempotent: deactivated rows stop
/// matching <c>IsActive</c>, so a redelivery reconciles only what is still active.
/// </remarks>
public class ChannelProductReconciliationService
{
    private readonly IChannelProductRepository _channelProductRepository;
    private readonly ILogger<ChannelProductReconciliationService> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="ChannelProductReconciliationService"/> class.
    /// </summary>
    /// <param name="channelProductRepository">Repository of the channel-product links.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="timeProvider">Clock source.</param>
    public ChannelProductReconciliationService(
        IChannelProductRepository channelProductRepository,
        ILogger<ChannelProductReconciliationService> logger,
        TimeProvider timeProvider)
    {
        _channelProductRepository = channelProductRepository;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Deactivates every link of the channel whose last sync predates the run's start.
    /// </summary>
    /// <param name="message">Reconciliation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of links deactivated.</returns>
    public async Task<int> ReconciliateAsync(
        ChannelProductReconciliationRequestedEvent message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // A default instant means the request carried no boundary. Reading it as "the beginning of
        // time" would deactivate every link of the channel, so the sweep is skipped instead.
        if (message.RunStartedAt == default)
        {
            _logger.LogWarning(
                "JobRun {JobRunId}: reconciliation request carries no RunStartedAt; skipping the sweep for ChannelId={ChannelId}.",
                message.JobRunId,
                message.ChannelId);

            return 0;
        }

        DateTimeOffset runStartedAt = message.RunStartedAt;

        IReadOnlyList<ChannelProduct> abandonedLinks = await _channelProductRepository
            .GetAbandonedLinksAsync(message.ChannelId, runStartedAt, cancellationToken);

        if (abandonedLinks.Count == 0)
        {
            _logger.LogInformation(
                "JobRun {JobRunId}: no abandoned links to deactivate for ChannelId={ChannelId} (run started {RunStartedAt}).",
                message.JobRunId,
                message.ChannelId,
                runStartedAt);

            return 0;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        foreach (ChannelProduct link in abandonedLinks)
        {
            link.IsActive = false;
            link.UpdatedAt = now;
        }

        await _channelProductRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "JobRun {JobRunId}: deactivated {Count} ChannelProduct link(s) for ChannelId={ChannelId} that were absent from the full listing imported since {RunStartedAt}.",
            message.JobRunId,
            abandonedLinks.Count,
            message.ChannelId,
            runStartedAt);

        return abandonedLinks.Count;
    }
}
