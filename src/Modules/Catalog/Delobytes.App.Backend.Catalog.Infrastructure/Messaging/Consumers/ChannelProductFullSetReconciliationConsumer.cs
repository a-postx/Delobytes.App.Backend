using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer for <see cref="ChannelProductFullSetReconciliationRequestedEvent"/>: deactivates the
/// links of a channel that the finished import run never observed, so
/// <see cref="ChannelProduct.IsActive"/> reports what the channel currently lists.
/// </summary>
/// <remarks>
/// Runs outside any batch, after Integrations has recorded every batch result of the run — which
/// happens strictly after each batch committed. That ordering is what makes the comparison below
/// correct; performed inside a batch transaction it read the siblings' uncommitted rows as dropped
/// cards and deactivated live links.
/// <para>
/// The sweep is idempotent: deactivated rows stop matching <c>IsActive</c>, so a redelivery
/// reconciles only what is still active.
/// </para>
/// </remarks>
public class ChannelProductFullSetReconciliationConsumer
{
    private readonly CatalogDbContext _context;
    private readonly ILogger<ChannelProductFullSetReconciliationConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="ChannelProductFullSetReconciliationConsumer"/> class.
    /// </summary>
    /// <param name="context">Catalog database context.</param>
    /// <param name="logger">Logger instance.</param>
    public ChannelProductFullSetReconciliationConsumer(
        CatalogDbContext context,
        ILogger<ChannelProductFullSetReconciliationConsumer> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Deactivates every link of the channel whose last sync predates the run's start.
    /// </summary>
    /// <param name="message">Reconciliation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of links deactivated.</returns>
    public async Task<int> ProcessAsync(
        ChannelProductFullSetReconciliationRequestedEvent message,
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

        // Links the run never touched keep a stamp from an earlier run, or none at all when they were
        // created outside any import. Only the former is evidence of absence: a link with no stamp
        // has never been seen by an import, so no run can claim to have missed it.
        List<ChannelProduct> abandonedLinks = await _context.ChannelProducts
            .Where(cp => cp.ChannelId == message.ChannelId
                && cp.IsActive
                && cp.LastSyncedAt != null
                && cp.LastSyncedAt < runStartedAt)
            .ToListAsync(cancellationToken);

        if (abandonedLinks.Count == 0)
        {
            _logger.LogInformation(
                "JobRun {JobRunId}: no abandoned links to deactivate for ChannelId={ChannelId} (run started {RunStartedAt}).",
                message.JobRunId,
                message.ChannelId,
                runStartedAt);

            return 0;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (ChannelProduct link in abandonedLinks)
        {
            link.IsActive = false;
            link.UpdatedAt = now;
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "JobRun {JobRunId}: deactivated {Count} ChannelProduct link(s) for ChannelId={ChannelId} that were absent from the full listing imported since {RunStartedAt}.",
            message.JobRunId,
            abandonedLinks.Count,
            message.ChannelId,
            runStartedAt);

        return abandonedLinks.Count;
    }
}
