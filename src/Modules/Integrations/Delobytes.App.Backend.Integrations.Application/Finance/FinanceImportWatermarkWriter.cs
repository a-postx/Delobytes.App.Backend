using Delobytes.App.Backend.Contracts.Identity;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Finance;

/// <summary>
/// Persists the finance watermark of one run, so the orchestrator and the completion consumer move
/// it through the same write instead of keeping two copies of the upsert.
/// </summary>
public sealed class FinanceImportWatermarkWriter
{
    private readonly ISyncCheckpointRepository _syncCheckpointRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FinanceImportWatermarkWriter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FinanceImportWatermarkWriter"/> class.
    /// </summary>
    /// <param name="syncCheckpointRepository">Watermark repository.</param>
    /// <param name="timeProvider">Clock source.</param>
    /// <param name="logger">Logger instance.</param>
    public FinanceImportWatermarkWriter(
        ISyncCheckpointRepository syncCheckpointRepository,
        TimeProvider timeProvider,
        ILogger<FinanceImportWatermarkWriter> logger)
    {
        _syncCheckpointRepository = syncCheckpointRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Moves the watermark of the run's connection and job type to the end of the imported window.
    /// </summary>
    /// <param name="jobRun">Run whose window was fully imported.</param>
    /// <param name="windowEnd">Last day of that window, channel-local.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WriteAsync(JobRun jobRun, DateOnly windowEnd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobRun);

        SyncCheckpoint? existing = await _syncCheckpointRepository.GetAsync(
            jobRun.ConnectionId, jobRun.JobType, cancellationToken);

        await _syncCheckpointRepository.UpsertAsync(
            new SyncCheckpoint
            {
                Id = existing?.Id ?? EntityId.New(),
                ConnectionId = jobRun.ConnectionId,
                JobType = jobRun.JobType,
                LastChangedAt = new DateTimeOffset(
                    windowEnd.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                LastSuccessAt = _timeProvider.GetUtcNow(),
                LastJobRunId = jobRun.Id,
            },
            cancellationToken);

        _logger.LogInformation(
            "Advanced finance watermark to {WindowEnd} for connection {ConnectionId}, job type {JobType}, JobRunId={JobRunId}.",
            windowEnd,
            jobRun.ConnectionId,
            jobRun.JobType,
            jobRun.Id);
    }
}
