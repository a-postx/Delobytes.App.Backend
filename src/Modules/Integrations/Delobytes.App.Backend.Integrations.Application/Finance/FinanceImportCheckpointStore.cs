using System.Text.Json;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Application.Finance;

/// <summary>
/// Reads and writes the finance import checkpoint payload for one job run, so the payload's shape
/// is owned here rather than restated by the orchestrator and the completion consumer.
/// </summary>
public sealed class FinanceImportCheckpointStore
{
    private readonly IJobCheckpointRepository _jobCheckpointRepository;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="FinanceImportCheckpointStore"/> class.
    /// </summary>
    /// <param name="jobCheckpointRepository">Checkpoint repository.</param>
    /// <param name="timeProvider">Clock source.</param>
    public FinanceImportCheckpointStore(
        IJobCheckpointRepository jobCheckpointRepository,
        TimeProvider timeProvider)
    {
        _jobCheckpointRepository = jobCheckpointRepository;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Loads the run's checkpoint.
    /// </summary>
    /// <remarks>
    /// A payload that exists but cannot be read fails the caller: treating it as absent restarts
    /// the window at page one, which republishes batches whose results the run has already counted.
    /// </remarks>
    /// <param name="jobRunId">Run the checkpoint belongs to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The payload, or null when the run has no checkpoint yet.</returns>
    public Task<FinanceImportCheckpointPayload?> LoadAsync(Guid jobRunId, CancellationToken cancellationToken)
    {
        return LoadCoreAsync(jobRunId, throwOnCorrupt: true, cancellationToken);
    }

    /// <summary>
    /// Loads the run's checkpoint for a reader that cannot repair it: an unreadable payload is
    /// reported as unknown instead of thrown, so one batch result does not fail over it.
    /// </summary>
    /// <param name="jobRunId">Run the checkpoint belongs to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The payload, or null when it is absent or unreadable.</returns>
    public Task<FinanceImportCheckpointPayload?> TryLoadAsync(Guid jobRunId, CancellationToken cancellationToken)
    {
        return LoadCoreAsync(jobRunId, throwOnCorrupt: false, cancellationToken);
    }

    /// <summary>
    /// Persists the run's checkpoint.
    /// </summary>
    /// <param name="jobRunId">Run the checkpoint belongs to.</param>
    /// <param name="payload">Payload to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SaveAsync(
        Guid jobRunId,
        FinanceImportCheckpointPayload payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        await _jobCheckpointRepository.UpsertAsync(
            new JobCheckpoint
            {
                JobRunId = jobRunId,
                Payload = JsonSerializer.Serialize(payload),
                UpdatedAt = _timeProvider.GetUtcNow(),
            },
            cancellationToken);
    }

    private async Task<FinanceImportCheckpointPayload?> LoadCoreAsync(
        Guid jobRunId,
        bool throwOnCorrupt,
        CancellationToken cancellationToken)
    {
        JobCheckpoint? checkpoint = await _jobCheckpointRepository
            .GetByJobRunIdAsync(jobRunId, cancellationToken);

        if (string.IsNullOrWhiteSpace(checkpoint?.Payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FinanceImportCheckpointPayload>(checkpoint.Payload)
                ?? throw new InvalidOperationException(
                    $"Finance import checkpoint payload of JobRun {jobRunId} deserialised to null.");
        }
        catch (JsonException exception) when (!throwOnCorrupt)
        {
            return null;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Finance import checkpoint payload of JobRun {jobRunId} is corrupt: {exception.Message}",
                exception);
        }
    }
}
