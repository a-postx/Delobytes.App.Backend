using Delobytes.App.Backend.Integrations.Application.Finance;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Aggregates the finance batch results of one run and, once every batch is confirmed, advances the
/// watermark. The watermark therefore never moves on a window that was only partially written.
/// </summary>
public class FinanceEntriesBatchImportCompletedConsumer : IConsumer<FinanceEntriesBatchImportCompletedEvent>
{
    private readonly ImportRunCompletion _completion;
    private readonly FinanceImportCheckpointStore _checkpoints;
    private readonly FinanceImportWatermarkWriter _watermarks;
    private readonly ILogger<FinanceEntriesBatchImportCompletedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FinanceEntriesBatchImportCompletedConsumer"/> class.
    /// </summary>
    /// <param name="completion">Batch aggregation and finalisation.</param>
    /// <param name="checkpoints">Finance import checkpoint store.</param>
    /// <param name="watermarks">Finance import watermark writer.</param>
    /// <param name="logger">Logger instance.</param>
    public FinanceEntriesBatchImportCompletedConsumer(
        ImportRunCompletion completion,
        FinanceImportCheckpointStore checkpoints,
        FinanceImportWatermarkWriter watermarks,
        ILogger<FinanceEntriesBatchImportCompletedConsumer> logger)
    {
        _completion = completion;
        _checkpoints = checkpoints;
        _watermarks = watermarks;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<FinanceEntriesBatchImportCompletedEvent> context)
    {
        FinanceEntriesBatchImportCompletedEvent message = context.Message;

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
                FallbackBatchCount: null,
                CanFinalize: true,
                RepublishesCompletionForRedelivery: false),
            context.CancellationToken);

        if (result == null)
        {
            _logger.LogWarning("JobRun {JobRunId} not found for finance batch result.", message.JobRunId);
            return;
        }

        await AccumulateDiagnosticsAsync(message, context.CancellationToken);

        if (result is { IsFinalized: true })
        {
            await AdvanceWatermarkAsync(result.JobRun, context.CancellationToken);
        }
    }

    private async Task AccumulateDiagnosticsAsync(
        FinanceEntriesBatchImportCompletedEvent message,
        CancellationToken cancellationToken)
    {
        FinanceImportCheckpointPayload? state = await _checkpoints
            .TryLoadAsync(message.JobRunId, cancellationToken);

        if (state == null)
        {
            return;
        }

        state.Diagnostics.Unattributed += message.Unattributed;
        state.Diagnostics.UnmatchedInstanceRows += message.UnmatchedInstanceRows;

        await _checkpoints.SaveAsync(message.JobRunId, state, cancellationToken);
    }

    private async Task AdvanceWatermarkAsync(JobRun run, CancellationToken cancellationToken)
    {
        FinanceImportCheckpointPayload? state = await _checkpoints
            .TryLoadAsync(run.Id, cancellationToken);

        if (state?.WindowEnd == null)
        {
            return;
        }

        await _watermarks.WriteAsync(run, state.WindowEnd.Value, cancellationToken);
    }
}
