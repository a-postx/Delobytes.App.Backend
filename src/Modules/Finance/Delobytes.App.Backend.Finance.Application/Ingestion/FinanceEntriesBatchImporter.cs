using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Journal;
using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Application.Projection;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Finance.Application.Ingestion;

/// <summary>
/// Imports one batch of channel finance entries: maps the contract to the journal, appends it,
/// recomputes the affected P&amp;L lines and reports the counters back to Integrations.
/// </summary>
/// <remarks>
/// The importer is idempotent by construction — the journal rejects a repeated idempotency key —
/// so a redelivered batch creates no new rows. It publishes the completed event after a partial
/// mapping failure too: a run that never receives a result would stay Running forever.
/// </remarks>
public class FinanceEntriesBatchImporter
{
    private readonly JournalAppender _appender;
    private readonly PnlLineProjector _projector;
    private readonly IPnlFeedStateStore _feedStateStore;
    private readonly IEventPublisher _eventPublisher;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FinanceEntriesBatchImporter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FinanceEntriesBatchImporter"/> class.
    /// </summary>
    /// <param name="appender">Journal appender.</param>
    /// <param name="projector">P&amp;L line projector.</param>
    /// <param name="feedStateStore">Per-channel feed checkpoint store.</param>
    /// <param name="eventPublisher">Event publisher.</param>
    /// <param name="tenantContext">Tenant context, used for diagnostics.</param>
    /// <param name="timeProvider">Clock source.</param>
    /// <param name="logger">Logger instance.</param>
    public FinanceEntriesBatchImporter(
        JournalAppender appender,
        PnlLineProjector projector,
        IPnlFeedStateStore feedStateStore,
        IEventPublisher eventPublisher,
        ITenantContext tenantContext,
        TimeProvider timeProvider,
        ILogger<FinanceEntriesBatchImporter> logger)
    {
        _appender = appender;
        _projector = projector;
        _feedStateStore = feedStateStore;
        _eventPublisher = eventPublisher;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Processes the FinanceEntriesBatchImportRequestedEvent message.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the batch is processed and the result is published.</returns>
    public async Task ProcessAsync(
        FinanceEntriesBatchImportRequestedEvent message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        _logger.LogInformation(
            "FinanceEntriesBatchImporter started processing batch. JobRunId={JobRunId}, BatchId={BatchId}, ChannelId={ChannelId}, TenantId={TenantId}, EntriesCount={EntriesCount}",
            message.JobRunId,
            message.BatchId,
            message.ChannelId,
            _tenantContext.TenantId,
            message.Entries.Count);

        DateTimeOffset now = _timeProvider.GetUtcNow();

        List<PnlEntry> entries = new();
        int failed = 0;

        foreach (ChannelFinanceEntrySnapshot snapshot in message.Entries)
        {
            try
            {
                entries.Add(FinanceEntryMapper.Map(snapshot, message.ChannelId, now));
            }
            catch (Exception ex)
            {
                failed++;

                _logger.LogError(
                    ex,
                    "Failed to map finance entry ExternalEntryId={ExternalEntryId} for JobRunId={JobRunId}",
                    snapshot.ExternalEntryId,
                    message.JobRunId);
            }
        }

        JournalAppendResult result = await _appender.AppendAsync(entries, cancellationToken);

        PnlRecomputeResult recomputeResult = await _projector.RecomputeAsync(
            result.AffectedOrderLineIds, cancellationToken);

        // Stamped after the work, not with the instant the batch was mapped: the heading shows when
        // the figures became available, and the projection is a part of that. Recorded for every
        // batch, including one that only produced duplicates вЂ” "arrived, nothing new" and "never
        // arrived" are different answers to the dashboard.
        await _feedStateStore.RecordBatchAsync(
            new PnlFeedCheckpoint(
                message.ChannelId,
                message.JobRunId,
                _timeProvider.GetUtcNow(),
                result.Appended,
                result.Duplicates,
                failed + recomputeResult.Failed),
            cancellationToken);

        FinanceEntriesBatchImportCompletedEvent completedEvent = new FinanceEntriesBatchImportCompletedEvent
        {
            JobRunId = message.JobRunId,
            BatchId = message.BatchId,
            Appended = result.Appended,
            Duplicates = result.Duplicates,
            Superseded = result.Superseded,
            Rejected = result.Rejected,
            Failed = failed,
            RecomputeFailed = recomputeResult.Failed,
            Unattributed = result.Unattributed,
            UnmatchedInstanceRows = result.UnmatchedInstanceRows,
        };

        await _eventPublisher.PublishAsync(completedEvent, cancellationToken);

        _logger.LogInformation(
            "FinanceEntriesBatchImporter completed batch. JobRunId={JobRunId}, BatchId={BatchId}, Appended={Appended}, Duplicates={Duplicates}, Superseded={Superseded}, Rejected={Rejected}, Failed={Failed}, RecomputeFailed={RecomputeFailed}, Unattributed={Unattributed}, UnmatchedInstanceRows={UnmatchedInstanceRows}",
            message.JobRunId,
            message.BatchId,
            result.Appended,
            result.Duplicates,
            result.Superseded,
            result.Rejected,
            failed,
            recomputeResult.Failed,
            result.Unattributed,
            result.UnmatchedInstanceRows);
    }
}
