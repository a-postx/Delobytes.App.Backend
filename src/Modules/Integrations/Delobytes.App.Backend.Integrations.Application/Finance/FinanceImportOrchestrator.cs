using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Identity;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Application.SalesChannels;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Application.Finance;

/// <summary>
/// Drives one finance import run: computes the window, reads the channel document page by page,
/// publishes bounded batches and records the mapping diagnostics. The watermark is deliberately not
/// advanced here: it moves only after every batch of the window is confirmed.
/// </summary>
/// <remarks>
/// <para>
/// The page cursor and the number of batches already published are written to the run's checkpoint
/// after every page, and each page is published as soon as it is read. A channel that paces its
/// API — the Wildberries weekly report allows one request per minute — defers the run between two
/// pages; without the checkpoint the retry would start the window over, re-read the first page
/// forever and never finish. The journal is idempotent by row identity, so re-reading a page after a
/// lost checkpoint write is harmless.
/// </para>
/// <para>
/// The run is marked Running before the first request, like the orders and products imports, so the
/// watchdog's type timeout covers the whole window including the channel's pauses. A throttle puts
/// it back to Pending with its deferral instant: the worker dispatches Pending runs only, and a
/// Running run that nobody re-dispatches would hold the tenant's parallelism slot until the
/// watchdog killed it.
/// </para>
/// </remarks>
public class FinanceImportOrchestrator
{
    private const int MaxPagesPerWindow = 10000;

    private readonly IJobRunRepository _jobRuns;
    private readonly FinanceImportCheckpointStore _checkpoints;
    private readonly ISyncCheckpointRepository _syncCheckpoints;
    private readonly FinanceImportWatermarkWriter _watermarks;
    private readonly IChannelApiClientFactory _clientFactory;
    private readonly IEventPublisher _eventPublisher;
    private readonly ImportRunCompletion _completion;
    private readonly IJobRunExecutionScope _executionScope;
    private readonly JobRunLifecycle _jobRunLifecycle;
    private readonly ITenantTimeService _tenantTimeService;
    private readonly ITenantContext _tenantContext;
    private readonly FinanceImportOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FinanceImportOrchestrator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FinanceImportOrchestrator"/> class.
    /// </summary>
    /// <param name="jobRuns">Job run repository.</param>
    /// <param name="checkpoints">Finance import checkpoint store.</param>
    /// <param name="syncCheckpoints">Watermark repository, read to plan the next window.</param>
    /// <param name="watermarks">Finance import watermark writer.</param>
    /// <param name="clientFactory">Resolves the API client of the run's channel.</param>
    /// <param name="eventPublisher">Event publisher.</param>
    /// <param name="completion">Batch-count announcement and finalisation.</param>
    /// <param name="executionScope">Ambient scope correlating the run's HTTP calls with the JobRun.</param>
    /// <param name="jobRunLifecycle">Job run status transitions.</param>
    /// <param name="tenantTimeService">Tenant time zone resolution.</param>
    /// <param name="tenantContext">Tenant context, used for diagnostics.</param>
    /// <param name="options">Finance import tuning.</param>
    /// <param name="timeProvider">Clock source.</param>
    /// <param name="logger">Logger instance.</param>
    public FinanceImportOrchestrator(
        IJobRunRepository jobRuns,
        FinanceImportCheckpointStore checkpoints,
        ISyncCheckpointRepository syncCheckpoints,
        FinanceImportWatermarkWriter watermarks,
        IChannelApiClientFactory clientFactory,
        IEventPublisher eventPublisher,
        ImportRunCompletion completion,
        IJobRunExecutionScope executionScope,
        JobRunLifecycle jobRunLifecycle,
        ITenantTimeService tenantTimeService,
        ITenantContext tenantContext,
        IOptions<FinanceImportOptions> options,
        TimeProvider timeProvider,
        ILogger<FinanceImportOrchestrator> logger)
    {
        _jobRuns = jobRuns;
        _checkpoints = checkpoints;
        _syncCheckpoints = syncCheckpoints;
        _watermarks = watermarks;
        _clientFactory = clientFactory;
        _eventPublisher = eventPublisher;
        _completion = completion;
        _executionScope = executionScope;
        _jobRunLifecycle = jobRunLifecycle;
        _tenantTimeService = tenantTimeService;
        _tenantContext = tenantContext;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Processes a dispatched finance import run.
    /// </summary>
    /// <param name="jobRunId">Run identifier.</param>
    /// <param name="connectionId">Connection identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ProcessAsync(Guid jobRunId, Guid connectionId, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "FinanceImportOrchestrator: JobRunId={JobRunId}, ConnectionId={ConnectionId}",
            jobRunId,
            connectionId);

        JobRun? run = await _jobRuns.GetByIdWithConnectionAsync(jobRunId, cancellationToken);

        if (run == null)
        {
            _logger.LogWarning("JobRun {JobRunId} not found, skipping", jobRunId);
            return;
        }

        if (run.Status is JobStatus.Success or JobStatus.Failed or JobStatus.Cancelled)
        {
            _logger.LogInformation(
                "JobRun {JobRunId} is in terminal status {Status}, skipping",
                jobRunId,
                run.Status);
            return;
        }

        Connection? connection = run.Connection;

        if (connection == null || !connection.IsActive)
        {
            string reason = connection == null ? "Connection not found" : "Connection is inactive";
            _logger.LogError("JobRun {JobRunId}: {Reason}. Marking as Failed.", jobRunId, reason);
            await _jobRunLifecycle.FailAsync(run, reason, cancellationToken);
            return;
        }

        SystemChannelTemplate? template = connection.SystemChannelTemplate;

        if (template == null)
        {
            await _jobRunLifecycle.FailAsync(
                run,
                "The job run's connection has no system channel template.",
                cancellationToken);
            return;
        }

        string channelCode = template.Code;

        // The factory is the one place that binds a client to its template; a finance run must not
        // reach the client any other way, or it would get an unbound instance that fails on the
        // first API call.
        IChannelApiClient apiClient;

        try
        {
            apiClient = _clientFactory.Create(template);
        }
        catch (NotSupportedException)
        {
            await _jobRunLifecycle.FailAsync(
                run, $"Channel '{channelCode}' is not supported.", cancellationToken);
            return;
        }

        if (apiClient is not IFinanceEntriesSource source)
        {
            await _jobRunLifecycle.FailAsync(
                run,
                $"Channel '{channelCode}' does not support finance import.",
                cancellationToken);
            return;
        }

        await _jobRunLifecycle.StartAsync(run, cancellationToken);

        if (run.NotBefore != null)
        {
            // The run has been dispatched, so its deferral is spent. A future NotBefore left on a
            // Running row would drop it out of IX_JobRuns_ActivePerConnectionType, which filters on
            // "NotBefore IS NULL", and the database would stop guarding against a second active run.
            run.NotBefore = null;
            await _jobRuns.UpdateAsync(run, cancellationToken);
        }

        // Correlates every HTTP call made through the finance source with this JobRun, so
        // RawApiResponseCaptureHandler can attribute captured rows to it.
        using (IDisposable scope = _executionScope.EnterScope(run.Id, connection.Id, connection.ChannelId))
        {
            try
            {
                await RunImportAsync(run, connection, source, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JobRun {JobRunId}: finance import failed", run.Id);

                await _jobRunLifecycle.FailAsync(run, ex.Message, cancellationToken);
            }
        }
    }

    private async Task RunImportAsync(
        JobRun run,
        Connection connection,
        IFinanceEntriesSource source,
        CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Cannot run a finance import without a resolved tenant.");

        TimeZoneInfo timeZone = await _tenantTimeService.GetTimeZoneAsync(tenantId, cancellationToken);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateOnly today = TenantTimeZones.DateAt(now, timeZone);

        FinanceImportCheckpointPayload? existingState = await _checkpoints.LoadAsync(run.Id, cancellationToken);

        (DateOnly From, DateOnly To)? planned = await ResolveWindowAsync(
            connection,
            run,
            existingState,
            today,
            cancellationToken);

        if (planned == null)
        {
            await CompleteEmptyAsync(run, now, cancellationToken);
            return;
        }

        (DateOnly From, DateOnly To) window = planned.Value;

        // A checkpoint taken for another window (a manual re-import of a different range, or a
        // window whose end moved at midnight) cannot be resumed: its cursor belongs to a document
        // that is not being read now.
        bool windowMatches = existingState != null
            && existingState.WindowFrom == window.From
            && existingState.WindowEnd == window.To;

        if (windowMatches && existingState!.WindowCompleted)
        {
            // The window was fully read before this dispatch: a redelivered event must not read it
            // again. Only the tail — the expected batch count and, when every result is already in,
            // the finalisation with the watermark move — still has to run.
            _logger.LogInformation(
                "Finance import window already fetched, finalising without re-reading it. JobRunId={JobRunId}",
                run.Id);

            await FinalizeFetchedWindowAsync(
                run,
                window,
                existingState.PublishedBatches,
                now,
                cancellationToken);

            return;
        }

        bool resumes = windowMatches && !string.IsNullOrEmpty(existingState!.Cursor);

        string? cursor = resumes ? existingState!.Cursor : null;
        int publishedBatches = resumes ? existingState!.PublishedBatches : 0;
        FinanceMappingDiagnostics diagnostics = resumes
            ? existingState!.Diagnostics
            : new FinanceMappingDiagnostics();

        int pages = 0;

        while (true)
        {
            FinanceEntriesPage page = await source.GetFinanceEntriesAsync(
                connection.Id,
                new FinanceEntriesRequest { From = window.From, To = window.To, Cursor = cursor },
                cancellationToken);

            if (page.Throttled)
            {
                // A rate-limited page is not a failure: the run keeps everything read so far in its
                // checkpoint and is re-dispatched once NotBefore passes. CancellationToken.None
                // because a cancelled consume must still persist the deferral, or the worker
                // re-publishes the run in a tight loop.
                await DeferAsync(run, page.RetryAt);
                return;
            }

            pages++;

            IReadOnlyList<IReadOnlyList<ChannelFinanceEntrySnapshot>> pageBatches =
                FinanceBatchSplitter.Split(page.Entries, _options.BatchSize);

            foreach (IReadOnlyList<ChannelFinanceEntrySnapshot> batch in pageBatches)
            {
                await _eventPublisher.PublishAsync(
                    new FinanceEntriesBatchImportRequestedEvent
                    {
                        JobRunId = run.Id,
                        ConnectionId = connection.Id,
                        ChannelId = connection.ChannelId,
                        BatchId = EntityId.New(),
                        Entries = batch.ToList(),
                    },
                    cancellationToken);
            }

            publishedBatches += pageBatches.Count;
            diagnostics.Add(page.Diagnostics);

            cursor = page.NextCursor;
            bool windowCompleted = page.IsComplete || string.IsNullOrEmpty(page.NextCursor);

            // Written after every page: this is what makes a throttled run resumable instead of
            // restarting the window from its first page on the next dispatch.
            await SaveStateAsync(
                run,
                window,
                windowCompleted ? null : cursor,
                publishedBatches,
                diagnostics,
                windowCompleted,
                existingState,
                cancellationToken);

            if (windowCompleted)
            {
                _logger.LogInformation(
                    "Finance import fetched {Batches} batch(es) in {Pages} page(s) this dispatch. JobRunId={JobRunId}, Window={From}..{To}",
                    publishedBatches,
                    pages,
                    run.Id,
                    window.From,
                    window.To);

                await FinalizeFetchedWindowAsync(run, window, publishedBatches, now, cancellationToken);
                return;
            }

            if (pages >= MaxPagesPerWindow)
            {
                // A safety valve, not a completion: the window is unfinished and the cursor is
                // saved, so the next dispatch continues from the same page.
                _logger.LogWarning(
                    "Finance import stopped after {Pages} page(s) in one dispatch; the rest of the window is deferred. JobRunId={JobRunId}",
                    pages,
                    run.Id);

                await DeferAsync(run, now.AddMinutes(1));
                return;
            }
        }
    }

    // Resolves the window the run covers, or null when nothing is due. An operator-triggered run
    // carries its explicit range in the checkpoint; a scheduled one is planned from the watermark.
    private async Task<(DateOnly From, DateOnly To)?> ResolveWindowAsync(
        Connection connection,
        JobRun run,
        FinanceImportCheckpointPayload? existingState,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        if (existingState?.ExplicitFrom is DateOnly explicitFrom
            && existingState.ExplicitTo is DateOnly explicitTo)
        {
            return (explicitFrom, explicitTo);
        }

        SyncCheckpoint? watermark = await _syncCheckpoints.GetAsync(
            connection.Id, run.JobType, cancellationToken);

        int initialBackfillDays = ResolveInitialBackfillDays(connection.SystemChannelTemplate.Code);

        DateOnly watermarkDate = watermark?.LastChangedAt is DateTimeOffset lastChanged
            ? DateOnly.FromDateTime(lastChanged.UtcDateTime)
            : today.AddDays(-Math.Max(0, initialBackfillDays));

        return FinanceImportWindowPlanner.Plan(
            connection.SystemChannelTemplate.Code,
            watermarkDate,
            today,
            initialBackfillDays,
            _options.WbOverlapDays,
            _options.OzonOverlapDays);
    }

    // Wildberries stops serving sales reports after 90 days, so a deeper initial window would only
    // spend the channel's two requests per day on periods that always come back empty.
    private int ResolveInitialBackfillDays(string channelCode)
    {
        if (string.Equals(channelCode, ChannelCatalog.WildberriesCode, StringComparison.OrdinalIgnoreCase))
        {
            return Math.Max(0, _options.WbInitialBackfillDays);
        }

        return Math.Max(0, _options.InitialBackfillDays);
    }

    // Runs the tail of a fully read window: the expected batch count is announced (which finalises
    // the run when every batch result is already recorded) and the watermark moves only once the
    // window's batches are confirmed. An empty window needs no batches at all.
    private async Task FinalizeFetchedWindowAsync(
        JobRun run,
        (DateOnly From, DateOnly To) window,
        int publishedBatches,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (publishedBatches == 0)
        {
            await _watermarks.WriteAsync(run, window.To, cancellationToken);
            await CompleteEmptyAsync(run, now, cancellationToken);
            return;
        }

        ImportRunResult? result = await _completion.AnnounceExpectedBatchCountAsync(
            run.Id,
            publishedBatches,
            canFinalize: true,
            cancellationToken);

        if (result is { IsFinalized: true })
        {
            await _watermarks.WriteAsync(run, window.To, cancellationToken);
        }
    }

    private Task SaveStateAsync(
        JobRun run,
        (DateOnly From, DateOnly To) window,
        string? cursor,
        int publishedBatches,
        FinanceMappingDiagnostics diagnostics,
        bool windowCompleted,
        FinanceImportCheckpointPayload? explicitRangeSource,
        CancellationToken cancellationToken)
    {
        FinanceImportCheckpointPayload state = new()
        {
            WindowFrom = window.From,
            WindowEnd = window.To,
            Cursor = cursor,
            PublishedBatches = publishedBatches,
            WindowCompleted = windowCompleted,
            Diagnostics = diagnostics,
            ExplicitFrom = explicitRangeSource?.ExplicitFrom,
            ExplicitTo = explicitRangeSource?.ExplicitTo,
        };

        return _checkpoints.SaveAsync(run.Id, state, cancellationToken);
    }

    private async Task CompleteEmptyAsync(JobRun run, DateTimeOffset now, CancellationToken cancellationToken)
    {
        run.Status = JobStatus.Success;
        run.CompletedAt = now;

        await _jobRuns.UpdateAsync(run, cancellationToken);
    }

    private async Task DeferAsync(JobRun run, DateTimeOffset? retryAt)
    {
        DateTimeOffset nextAttemptAt = retryAt ?? _timeProvider.GetUtcNow().AddMinutes(1);

        run.NotBefore = nextAttemptAt;

        // Back to Pending, which is the only status the worker dispatches. Left Running, a deferred
        // run is re-dispatched by nobody — the scheduler refuses to create a second run while this
        // one is active — so it would sit until the watchdog killed it on the running timeout,
        // occupying the tenant's parallelism slot and starving every other import meanwhile. The
        // next dispatch starts it again and clears the deferral.
        run.Status = JobStatus.Pending;

        // CancellationToken.None on purpose: if the consume is cancelled while the API is
        // throttled, the deferral must still be persisted, otherwise the run stays immediately due
        // and the worker re-publishes it on every tick.
        await _jobRuns.UpdateAsync(run, CancellationToken.None);

        _logger.LogInformation(
            "Finance import deferred: the channel rate limiter allows the next attempt at {NextAttemptAt:O}. JobRunId={JobRunId}",
            nextAttemptAt,
            run.Id);
    }
}
