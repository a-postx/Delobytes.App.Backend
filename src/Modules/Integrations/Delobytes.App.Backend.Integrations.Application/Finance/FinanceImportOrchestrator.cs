using System.Text.Json;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Application.Finance;

/// <summary>
/// Drives one finance import run: computes the window, reads the channel document page by page,
/// publishes bounded batches and records the mapping diagnostics. The watermark is deliberately not
/// advanced here: it moves only after every batch of the window is confirmed.
/// </summary>
public class FinanceImportOrchestrator
{
    private const int MaxPagesPerWindow = 10000;

    private readonly IJobRunRepository _jobRuns;
    private readonly IJobCheckpointRepository _checkpoints;
    private readonly ISyncCheckpointRepository _watermarks;
    private readonly IServiceProvider _services;
    private readonly IEventPublisher _eventPublisher;
    private readonly ITenantTimeService _tenantTimeService;
    private readonly ITenantContext _tenantContext;
    private readonly FinanceImportOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FinanceImportOrchestrator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FinanceImportOrchestrator"/> class.
    /// </summary>
    public FinanceImportOrchestrator(
        IJobRunRepository jobRuns,
        IJobCheckpointRepository checkpoints,
        ISyncCheckpointRepository watermarks,
        IServiceProvider services,
        IEventPublisher eventPublisher,
        ITenantTimeService tenantTimeService,
        ITenantContext tenantContext,
        IOptions<FinanceImportOptions> options,
        TimeProvider timeProvider,
        ILogger<FinanceImportOrchestrator> logger)
    {
        _jobRuns = jobRuns;
        _checkpoints = checkpoints;
        _watermarks = watermarks;
        _services = services;
        _eventPublisher = eventPublisher;
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
        JobRun? run = await _jobRuns.GetByIdWithConnectionAsync(jobRunId, cancellationToken);

        if (run == null || run.Status is JobStatus.Success or JobStatus.Failed or JobStatus.Cancelled)
        {
            return;
        }

        string? channelCode = run.Connection.SystemChannelTemplate?.Code;

        IFinanceEntriesSource? source = string.IsNullOrEmpty(channelCode)
            ? null
            : _services.GetKeyedService<IFinanceEntriesSource>(channelCode);

        if (source == null)
        {
            await FailAsync(run, $"No finance source is registered for channel '{channelCode}'.", cancellationToken);
            return;
        }

        Guid tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Cannot run a finance import without a resolved tenant.");

        TimeZoneInfo timeZone = await _tenantTimeService.GetTimeZoneAsync(tenantId, cancellationToken);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateOnly today = TenantTimeZones.DateAt(now, timeZone);

        JobCheckpoint? existingCheckpoint = await _checkpoints.GetByJobRunIdAsync(jobRunId, cancellationToken);
        RunState? existingState = Deserialize(existingCheckpoint?.Payload);

        (DateOnly From, DateOnly To) window;

        if (existingState?.ExplicitFrom is DateOnly explicitFrom && existingState.ExplicitTo is DateOnly explicitTo)
        {
            window = (explicitFrom, explicitTo);
        }
        else
        {
            SyncCheckpoint? watermark = await _watermarks.GetAsync(connectionId, run.JobType, cancellationToken);

            DateOnly watermarkDate = watermark?.LastChangedAt is DateTimeOffset lastChanged
                ? DateOnly.FromDateTime(lastChanged.UtcDateTime)
                : today.AddDays(-Math.Max(0, _options.InitialBackfillDays));

            (DateOnly From, DateOnly To)? planned = FinanceImportWindowPlanner.Plan(
                channelCode!,
                watermarkDate,
                today,
                _options.InitialBackfillDays,
                _options.WbOverlapDays,
                _options.OzonOverlapDays);

            if (planned == null)
            {
                await CompleteEmptyAsync(run, now, cancellationToken);
                return;
            }

            window = planned.Value;
        }

        List<ChannelFinanceEntrySnapshot> entries = new();
        FinanceMappingDiagnostics diagnostics = new();

        string? cursor = null;
        int pages = 0;

        while (true)
        {
            FinanceEntriesPage page = await source.GetFinanceEntriesAsync(
                connectionId,
                new FinanceEntriesRequest { From = window.From, To = window.To, Cursor = cursor },
                cancellationToken);

            if (page.Throttled)
            {
                await FailAsync(
                    run,
                    $"Finance API is rate limited, next attempt at {page.RetryAt:O}.",
                    cancellationToken);
                return;
            }

            entries.AddRange(page.Entries);
            diagnostics.Add(page.Diagnostics);

            pages++;

            if (page.IsComplete || string.IsNullOrEmpty(page.NextCursor) || pages >= MaxPagesPerWindow)
            {
                break;
            }

            cursor = page.NextCursor;
        }

        IReadOnlyList<IReadOnlyList<ChannelFinanceEntrySnapshot>> batches =
            FinanceBatchSplitter.Split(entries, _options.BatchSize);

        List<Guid> batchIds = new(batches.Count);

        foreach (IReadOnlyList<ChannelFinanceEntrySnapshot> batch in batches)
        {
            Guid batchId = Guid.NewGuid();
            batchIds.Add(batchId);

            await _eventPublisher.PublishAsync(
                new FinanceEntriesBatchImportRequestedEvent
                {
                    JobRunId = jobRunId,
                    ConnectionId = connectionId,
                    ChannelId = run.Connection.ChannelId,
                    BatchId = batchId,
                    Entries = batch.ToList(),
                },
                cancellationToken);
        }

        RunState state = new()
        {
            WindowEnd = window.To,
            BatchIds = batchIds,
            Diagnostics = diagnostics,
            ExplicitFrom = existingState?.ExplicitFrom,
            ExplicitTo = existingState?.ExplicitTo,
        };

        await _checkpoints.UpsertAsync(
            new JobCheckpoint
            {
                JobRunId = jobRunId,
                Payload = JsonSerializer.Serialize(state),
                UpdatedAt = now,
            },
            cancellationToken);

        run.ExpectedBatchCount = batches.Count;

        if (run.Status == JobStatus.Pending)
        {
            run.Status = JobStatus.Running;
            run.StartedAt = now;
        }

        await _jobRuns.UpdateAsync(run, cancellationToken);

        _logger.LogInformation(
            "Finance import fetched {Entries} entries in {Batches} batch(es). JobRunId={JobRunId}, Window={From}..{To}, Pages={Pages}",
            entries.Count,
            batches.Count,
            jobRunId,
            window.From,
            window.To,
            pages);

        if (batches.Count == 0)
        {
            await AdvanceWatermarkAsync(run, state, now, cancellationToken);
            await CompleteEmptyAsync(run, now, cancellationToken);
        }
    }

    private async Task CompleteEmptyAsync(JobRun run, DateTimeOffset now, CancellationToken cancellationToken)
    {
        run.Status = JobStatus.Success;
        run.CompletedAt = now;

        await _jobRuns.UpdateAsync(run, cancellationToken);
    }

    private async Task AdvanceWatermarkAsync(
        JobRun run,
        RunState state,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (state.WindowEnd == null)
        {
            return;
        }

        SyncCheckpoint? existing = await _watermarks.GetAsync(run.ConnectionId, run.JobType, cancellationToken);

        await _watermarks.UpsertAsync(
            new SyncCheckpoint
            {
                Id = existing?.Id ?? Guid.NewGuid(),
                ConnectionId = run.ConnectionId,
                JobType = run.JobType,
                LastChangedAt = new DateTimeOffset(
                    state.WindowEnd.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                LastSuccessAt = now,
                LastJobRunId = run.Id,
            },
            cancellationToken);
    }

    private async Task FailAsync(JobRun run, string error, CancellationToken cancellationToken)
    {
        run.Status = JobStatus.Failed;
        run.Error = error;
        run.CompletedAt = _timeProvider.GetUtcNow();

        await _jobRuns.UpdateAsync(run, CancellationToken.None);
    }

    private static RunState? Deserialize(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RunState>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Durable state of one finance run: the window it covers and the batches it expects.
    /// </summary>
    public sealed class RunState
    {
        public DateOnly? WindowEnd { get; set; }

        public List<Guid> BatchIds { get; set; } = new();

        public FinanceMappingDiagnostics Diagnostics { get; set; } = new();

        public DateOnly? ExplicitFrom { get; set; }

        public DateOnly? ExplicitTo { get; set; }
    }
}
