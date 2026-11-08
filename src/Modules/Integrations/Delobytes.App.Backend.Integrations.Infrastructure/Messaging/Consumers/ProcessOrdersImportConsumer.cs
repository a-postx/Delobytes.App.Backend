using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Orders;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Orchestrates an orders import run: resolves the fetchers for the connection's channel and the
/// job run's job type, picks the backfill or delta branch, awaits it once, and turns its outcome
/// into a checkpoint, a continuation or a failure. Which fetch algorithm runs, what it costs and
/// what a stop means for it belong to the resolved <see cref="IOrdersFetchers"/>.
/// </summary>
public class ProcessOrdersImportConsumer
{
    private readonly IJobRunRepository _jobRunRepository;
    private readonly OrdersImportCheckpointStore _checkpointStore;
    private readonly OrdersBackfillWalker _backfillWalker;
    private readonly IOrdersFetcherResolver _fetcherResolver;
    private readonly JobRunLifecycle _jobRunLifecycle;
    private readonly OrdersContinuationScheduler _continuationScheduler;
    private readonly IChannelApiClientFactory _clientFactory;
    private readonly IEventPublisher _eventPublisher;
    private readonly JobRunExecutionContext _jobRunExecutionContext;
    private readonly ILogger<ProcessOrdersImportConsumer> _logger;
    private readonly TimeProvider _timeProvider;

    public ProcessOrdersImportConsumer(
        IJobRunRepository jobRunRepository,
        OrdersImportCheckpointStore checkpointStore,
        OrdersBackfillWalker backfillWalker,
        IOrdersFetcherResolver fetcherResolver,
        IChannelApiClientFactory clientFactory,
        IEventPublisher eventPublisher,
        JobRunExecutionContext jobRunExecutionContext,
        ILogger<ProcessOrdersImportConsumer> logger,
        TimeProvider timeProvider,
        JobRunLifecycle jobRunLifecycle,
        OrdersContinuationScheduler continuationScheduler)
    {
        _jobRunRepository = jobRunRepository;
        _checkpointStore = checkpointStore;
        _backfillWalker = backfillWalker;
        _fetcherResolver = fetcherResolver;
        _jobRunLifecycle = jobRunLifecycle;
        _continuationScheduler = continuationScheduler;
        _clientFactory = clientFactory;
        _eventPublisher = eventPublisher;
        _jobRunExecutionContext = jobRunExecutionContext;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task ProcessAsync(OrdersImportRequestedEvent message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ProcessOrdersImportConsumer: JobRunId={JobRunId}, ConnectionId={ConnectionId}",
            message.JobRunId,
            message.ConnectionId);

        JobRun? jobRun = await _jobRunRepository.GetByIdWithConnectionAsync(message.JobRunId, cancellationToken);

        if (jobRun == null)
        {
            _logger.LogWarning("JobRun {JobRunId} not found, skipping", message.JobRunId);
            return;
        }

        if (jobRun.Status != JobStatus.Pending && jobRun.Status != JobStatus.Running)
        {
            _logger.LogWarning(
                "JobRun {JobRunId} is in terminal status {Status}, skipping",
                jobRun.Id,
                jobRun.Status);
            return;
        }

        Connection? connection = jobRun.Connection;

        if (connection == null || !connection.IsActive)
        {
            string reason = connection == null ? "Connection not found" : "Connection is inactive";
            _logger.LogError("JobRun {JobRunId}: {Reason}. Marking as Failed.", jobRun.Id, reason);
            await _jobRunLifecycle.FailAsync(jobRun, reason, cancellationToken);
            return;
        }

        // Resolution happens before the run is marked Running: a combination it cannot serve is a
        // configuration error that fails the run outright, not a run that starts and then stops.
        string channelCode = connection.SystemChannelTemplate.Code;

        OrdersFetchersResolution resolution = _fetcherResolver.Resolve(channelCode, jobRun.JobType);

        if (resolution is not OrdersFetchersResolution.Supported supported)
        {
            string error = ((OrdersFetchersResolution.Unsupported)resolution).Reason;
            _logger.LogError("JobRun {JobRunId}: {Error}", jobRun.Id, error);
            await _jobRunLifecycle.FailAsync(jobRun, error, cancellationToken);
            return;
        }

        IOrdersFetchers fetchers = supported.Fetchers;

        // The budget comes from the resolved channel: Ozon paces a cursor walk where Wildberries
        // paces a bisection, so one shared constant would be wrong for at least one of them.
        DateTimeOffset deadline = _timeProvider.GetUtcNow() + fetchers.RunBudget;

        await _jobRunLifecycle.StartAsync(jobRun, cancellationToken);

        using (IDisposable scope = _jobRunExecutionContext.EnterScope(jobRun.Id))
        {
            try
            {
                await RunImportAsync(jobRun, connection, fetchers, deadline, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JobRun {JobRunId}: orders import failed", jobRun.Id);

                await _jobRunLifecycle.FailAsync(jobRun, ex.Message, cancellationToken);
            }
        }
    }

    private async Task RunImportAsync(
        JobRun jobRun,
        Connection connection,
        IOrdersFetchers fetchers,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        IChannelApiClient apiClient = _clientFactory.Create(connection.SystemChannelTemplate);

        if (apiClient is not IOrdersSource ordersSource)
        {
            string error = $"Channel '{connection.SystemChannelTemplate.Code}' does not support orders import";
            _logger.LogError("JobRun {JobRunId}: {Error}", jobRun.Id, error);
            await _jobRunLifecycle.FailAsync(jobRun, error, cancellationToken);
            return;
        }

        OrdersImportCheckpointPayload? previousPayload = await _checkpointStore
            .LoadAsync(jobRun.Id, cancellationToken);

        if (previousPayload != null && previousPayload.Phase == OrdersImportCheckpointPhase.FetchCompleted)
        {
            await PublishFetchCompletedForFinishedRunAsync(jobRun, connection, previousPayload, cancellationToken);
            return;
        }

        (OrdersImportContext context, int nextBatchIndex) = await CreateContextAsync(
            jobRun, connection, deadline, ordersSource, fetchers, previousPayload, cancellationToken);

        RunCounters counters = new RunCounters();
        BatchSequence batches = new BatchSequence(nextBatchIndex);

        IOrdersFetcher fetcher = context.IsBackfill ? fetchers.Backfill : fetchers.Delta;

        FetchOutcome outcome = await fetcher.FetchAsync(context, counters, batches, cancellationToken);

        switch (outcome)
        {
            case FetchOutcome.Stopped stopped:
                await FinishStoppedAsync(context, counters, batches, stopped, cancellationToken);
                break;
            case FetchOutcome.BackfillRead backfill:
                await FinishCompletedAsync(context, counters, batches, backfill, cancellationToken);
                break;
            case FetchOutcome.DeltaRead delta:
                await FinishCompletedAsync(context, counters, batches, delta, cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Unhandled fetch outcome {outcome.GetType().Name}.");
        }
    }

    private async Task<(OrdersImportContext Context, int NextBatchIndex)> CreateContextAsync(
        JobRun jobRun,
        Connection connection,
        DateTimeOffset deadline,
        IOrdersSource ordersSource,
        IOrdersFetchers fetchers,
        OrdersImportCheckpointPayload? previousPayload,
        CancellationToken cancellationToken)
    {
        // A redelivered run must re-read the same window, otherwise the batch count persisted for
        // the aggregator would no longer match what is being republished.
        DateTimeOffset windowTo = previousPayload?.WindowTo ?? _timeProvider.GetUtcNow();

        OrdersBackfillState backfillState = await _backfillWalker
            .GetOrCreateAsync(connection.Id, jobRun.JobType, fetchers.Tuning, cancellationToken);

        // The walk decides the branch, not the watermark. A connection that was syncing under
        // the old single-request backfill has an incomplete history precisely because that
        // request imported one calendar day out of ninety, so it earns the walk too. Re-reading
        // stored days is idempotent on the (ChannelId, ExternalOrderId) upsert; leaving the hole
        // in place is not recoverable.
        bool isBackfill = previousPayload?.IsBackfill == true || !backfillState.IsCompleted;

        // A redelivered message continues batch numbering where the last attempt left off: an
        // InProgress or Stopped attempt published some units and persisted their count, but never
        // reached the finishing write. Starting over at 0 would reuse BatchIds already recorded
        // for different content under this JobRunId.
        int nextBatchIndex = previousPayload != null
            && previousPayload.Phase is OrdersImportCheckpointPhase.InProgress or OrdersImportCheckpointPhase.Stopped
            ? previousPayload.TotalBatches
            : 0;

        OrdersImportContext context = new OrdersImportContext
        {
            JobRun = jobRun,
            Connection = connection,
            OrdersSource = ordersSource,
            BackfillState = backfillState,
            Tuning = fetchers.Tuning,
            Deadline = deadline,
            WindowTo = windowTo,
            IsBackfill = isBackfill,
            Schema = ResolveSchema(jobRun.JobType),
        };

        if (previousPayload == null)
        {
            await _checkpointStore.SaveStartedAsync(jobRun.Id, context, cancellationToken);
        }

        return (context, nextBatchIndex);
    }

    /// <summary>
    /// Maps the job type to the orders schema the channel must request, so a schema a channel does
    /// not implement fails at the API boundary instead of being read as its default schema.
    /// </summary>
    private static OrdersSchema? ResolveSchema(JobType jobType)
    {
        return jobType switch
        {
            JobType.OzonOrdersImportFbo => OrdersSchema.Fbo,
            JobType.OzonOrdersImportFbs => OrdersSchema.Fbs,
            _ => null,
        };
    }

    /// <summary>
    /// A run stopped by throttle or budget leaves its data incomplete, so it neither finalises the
    /// job (no FetchCompleted phase, no completion event) nor reports a failure: it persists the
    /// progress checkpoint and schedules a continuation.
    /// </summary>
    private async Task FinishStoppedAsync(
        OrdersImportContext context,
        RunCounters counters,
        BatchSequence batches,
        FetchOutcome.Stopped outcome,
        CancellationToken cancellationToken)
    {
        await _checkpointStore.SaveStoppedAsync(
            context.JobRun.Id, context, counters, batches, cancellationToken);

        JobRun? continuation = await _continuationScheduler
            .ScheduleAsync(context.JobRun, outcome.ResumeAt, cancellationToken);

        Guid? continuationId = continuation?.Id;
        DateTimeOffset? continuationNotBefore = continuation?.NotBefore;

        _logger.LogWarning(
            "JobRun {JobRunId}: stopped, backfill={Backfill}, stopCause={StopCause}, "
            + "requests={Requests}, resumeAt={ResumeAt}, inProcessWaits={InProcessWaits}, "
            + "totalWaitedSeconds={TotalWaitedSeconds}, continuationId={ContinuationId}, "
            + "continuationNotBefore={ContinuationNotBefore}",
            context.JobRun.Id,
            context.IsBackfill,
            outcome.Cause,
            outcome.RequestsIssued,
            outcome.ResumeAt,
            counters.InProcessWaits,
            counters.TotalWaited.TotalSeconds,
            continuationId,
            continuationNotBefore);
    }

    private async Task FinishCompletedAsync(
        OrdersImportContext context,
        RunCounters counters,
        BatchSequence batches,
        FetchOutcome.DeltaRead outcome,
        CancellationToken cancellationToken)
    {
        await PersistCompletedAsync(
            context,
            counters,
            batches,
            outcome.MaxChangedAt,
            isBackfillCompleted: false,
            handoverWatermark: null,
            outcome.Orders.Count,
            cancellationToken);
    }

    private async Task FinishCompletedAsync(
        OrdersImportContext context,
        RunCounters counters,
        BatchSequence batches,
        FetchOutcome.BackfillRead outcome,
        CancellationToken cancellationToken)
    {
        await PersistCompletedAsync(
            context,
            counters,
            batches,
            maxChangedAt: null,
            isBackfillCompleted: context.IsBackfill && outcome.IsCompleted,
            handoverWatermark: context.IsBackfill ? outcome.HandoverWatermark : null,
            outcome.OrdersFetched,
            cancellationToken);
    }

    private async Task PersistCompletedAsync(
        OrdersImportContext context,
        RunCounters counters,
        BatchSequence batches,
        DateTimeOffset? maxChangedAt,
        bool isBackfillCompleted,
        DateTimeOffset? handoverWatermark,
        int rows,
        CancellationToken cancellationToken)
    {
        // The final count must be persisted before the completion event is published: the
        // aggregator finalises on whichever of the completion event or a batch result brings its
        // recorded count up to the expected total, and it can only do that once it knows the
        // final count.
        await _checkpointStore.SaveCompletedAsync(
            context.JobRun.Id,
            context,
            counters,
            batches,
            maxChangedAt,
            isBackfillCompleted,
            handoverWatermark,
            cancellationToken);

        await _eventPublisher.PublishAsync(
            new OrdersImportFetchCompletedEvent
            {
                JobRunId = context.JobRun.Id,
                ConnectionId = context.Connection.Id,
                TotalBatches = batches.NextIndex,
                MaxChangedAt = maxChangedAt,
                HandoverWatermark = handoverWatermark,
                IsBackfill = context.IsBackfill,
                IsBackfillCompleted = isBackfillCompleted,
                PagesFetched = counters.RequestsIssued,
                WindowTo = context.WindowTo,
            },
            cancellationToken);

        _logger.LogInformation(
            "JobRun {JobRunId}: completed, backfill={Backfill}, rows={Rows}, requests={Requests}, "
            + "published {Batches} batches, inProcessWaits={InProcessWaits}, totalWaitedSeconds={TotalWaitedSeconds}",
            context.JobRun.Id,
            context.IsBackfill,
            rows,
            counters.RequestsIssued,
            batches.NextIndex,
            counters.InProcessWaits,
            counters.TotalWaited.TotalSeconds);
    }

    private async Task PublishFetchCompletedForFinishedRunAsync(
        JobRun jobRun,
        Connection connection,
        OrdersImportCheckpointPayload previousPayload,
        CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishAsync(
            BuildFetchCompletedEvent(jobRun.Id, connection.Id, previousPayload),
            cancellationToken);
    }

    private static OrdersImportFetchCompletedEvent BuildFetchCompletedEvent(
        Guid jobRunId,
        Guid connectionId,
        OrdersImportCheckpointPayload payload)
    {
        return new OrdersImportFetchCompletedEvent
        {
            JobRunId = jobRunId,
            ConnectionId = connectionId,
            TotalBatches = payload.TotalBatches,
            MaxChangedAt = payload.MaxChangedAt,
            HandoverWatermark = payload.HandoverWatermark,
            IsBackfill = payload.IsBackfill,
            IsBackfillCompleted = payload.IsBackfillCompleted,
            PagesFetched = payload.PagesFetched,
            WindowTo = payload.WindowTo,
        };
    }
}
