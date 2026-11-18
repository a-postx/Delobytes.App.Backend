using System.Text.Json;
using Delobytes.App.Backend.Integrations.Application.Finance;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Aggregates the finance batch results of one run and, once every batch is confirmed, advances the
/// watermark. The watermark therefore never moves on a window that was only partially written.
/// </summary>
public class FinanceEntriesBatchImportCompletedConsumer : IConsumer<FinanceEntriesBatchImportCompletedEvent>
{
    private readonly IJobRunRepository _jobRuns;
    private readonly IJobCheckpointRepository _checkpoints;
    private readonly ISyncCheckpointRepository _watermarks;
    private readonly ImportRunCompletion _completion;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FinanceEntriesBatchImportCompletedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FinanceEntriesBatchImportCompletedConsumer"/> class.
    /// </summary>
    public FinanceEntriesBatchImportCompletedConsumer(
        IJobRunRepository jobRuns,
        IJobCheckpointRepository checkpoints,
        ISyncCheckpointRepository watermarks,
        ImportRunCompletion completion,
        TimeProvider timeProvider,
        ILogger<FinanceEntriesBatchImportCompletedConsumer> logger)
    {
        _jobRuns = jobRuns;
        _checkpoints = checkpoints;
        _watermarks = watermarks;
        _completion = completion;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<FinanceEntriesBatchImportCompletedEvent> context)
    {
        FinanceEntriesBatchImportCompletedEvent message = context.Message;

        JobRun? run = await _jobRuns.GetByIdAsync(message.JobRunId, context.CancellationToken);

        if (run == null)
        {
            _logger.LogWarning("JobRun {JobRunId} not found for finance batch result.", message.JobRunId);
            return;
        }

        ImportRunResult? result = await _completion.RecordBatchAsync(
            new ImportBatchDelivery(
                message.JobRunId,
                message.BatchId,
                RecordsProcessed: message.Appended + message.Duplicates + message.Superseded,
                RecordsCreated: message.Appended,
                RecordsUpdated: 0,
                RecordsSkipped: message.Duplicates + message.Superseded + message.Rejected,
                RecordsFailed: message.Failed,
                ErrorMessage: null,
                IsLastBatch: false,
                ExpectedBatchCount: null,
                FallbackBatchCount: run.ExpectedBatchCount,
                CanFinalize: true,
                RepublishesCompletionForRedelivery: false),
            context.CancellationToken);

        await AccumulateDiagnosticsAsync(message, context.CancellationToken);

        if (result is { IsFinalized: true })
        {
            await AdvanceWatermarkAsync(run, context.CancellationToken);
        }
    }

    private async Task AccumulateDiagnosticsAsync(
        FinanceEntriesBatchImportCompletedEvent message,
        CancellationToken cancellationToken)
    {
        JobCheckpoint? checkpoint = await _checkpoints.GetByJobRunIdAsync(message.JobRunId, cancellationToken);

        if (checkpoint == null)
        {
            return;
        }

        FinanceImportOrchestrator.RunState? state = Deserialize(checkpoint.Payload);

        if (state == null)
        {
            return;
        }

        state.Diagnostics.Unattributed += message.Unattributed;
        state.Diagnostics.UnmatchedInstanceRows += message.UnmatchedInstanceRows;

        checkpoint.Payload = JsonSerializer.Serialize(state);
        checkpoint.UpdatedAt = _timeProvider.GetUtcNow();

        await _checkpoints.UpsertAsync(checkpoint, cancellationToken);
    }

    private async Task AdvanceWatermarkAsync(JobRun run, CancellationToken cancellationToken)
    {
        JobCheckpoint? checkpoint = await _checkpoints.GetByJobRunIdAsync(run.Id, cancellationToken);
        FinanceImportOrchestrator.RunState? state = checkpoint == null ? null : Deserialize(checkpoint.Payload);

        if (state?.WindowEnd == null)
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
                LastSuccessAt = _timeProvider.GetUtcNow(),
                LastJobRunId = run.Id,
            },
            cancellationToken);

        _logger.LogInformation(
            "Advanced finance watermark to {WindowEnd} for connection {ConnectionId}, job type {JobType}.",
            state.WindowEnd,
            run.ConnectionId,
            run.JobType);
    }

    private static FinanceImportOrchestrator.RunState? Deserialize(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FinanceImportOrchestrator.RunState>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
