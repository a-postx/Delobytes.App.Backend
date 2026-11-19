using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Tests.Finance;

/// <summary>
/// Minimal in-memory stand-in for the job-run repository: enough to drive the orchestrator and the
/// completion path, with batch results counted the way the database counts them.
/// </summary>
internal sealed class FakeJobRunRepository : IJobRunRepository
{
    private readonly Dictionary<Guid, JobRun> _runs = new();
    private readonly List<JobRunBatchResult> _batchResults = new();

    public void Seed(JobRun run)
    {
        _runs[run.Id] = run;
    }

    public void RecordBatchResult(Guid jobRunId, Guid messageId, int created, int skipped, int failed)
    {
        _batchResults.Add(new JobRunBatchResult
        {
            Id = Guid.NewGuid(),
            JobRunId = jobRunId,
            MessageId = messageId,
            RecordsProcessed = created + skipped + failed,
            RecordsCreated = created,
            RecordsSkipped = skipped,
            RecordsFailed = failed,
            ReceivedAt = DateTimeOffset.UtcNow,
        });
    }

    public Task<JobRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _runs.TryGetValue(id, out JobRun? run);

        return Task.FromResult(run);
    }

    public Task<JobRun> CreateAsync(JobRun jobRun, CancellationToken cancellationToken)
    {
        _runs[jobRun.Id] = jobRun;

        return Task.FromResult(jobRun);
    }

    public Task UpdateAsync(JobRun jobRun, CancellationToken cancellationToken)
    {
        _runs[jobRun.Id] = jobRun;

        return Task.CompletedTask;
    }

    public Task<bool> TryUpdateAsync(JobRun jobRun, CancellationToken cancellationToken)
    {
        _runs[jobRun.Id] = jobRun;

        return Task.FromResult(true);
    }

    public Task<(int TotalCount, List<JobRun> Items)> GetPagedAsync(
        JobType? jobType,
        Guid? connectionId,
        JobStatus[]? statuses,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<bool> HasActiveJobAsync(Guid connectionId, JobType jobType, CancellationToken cancellationToken)
    {
        bool active = _runs.Values.Any(run =>
            run.ConnectionId == connectionId
            && run.JobType == jobType
            && run.Status is JobStatus.Pending or JobStatus.Running);

        return Task.FromResult(active);
    }

    public Task<bool> HasActiveOzonOrdersJobAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    public Task<List<JobRun>> GetPendingJobsAsync(
        Guid tenantId, int limit, DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<JobRun> pending = _runs.Values
            .Where(run => run.Status == JobStatus.Pending && (run.NotBefore == null || run.NotBefore <= now))
            .Take(limit)
            .ToList();

        return Task.FromResult(pending);
    }

    public Task<bool> HasPendingJobAsync(
        Guid connectionId, JobType jobType, Guid excludeJobRunId, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    public Task<int> CountRunningJobsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_runs.Values.Count(run => run.Status == JobStatus.Running));
    }

    public Task<List<JobRun>> GetRunningJobsOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        return Task.FromResult(new List<JobRun>());
    }

    public Task<List<JobRun>> GetPendingJobsOlderThanAsync(
        DateTimeOffset createdBefore, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return Task.FromResult(new List<JobRun>());
    }

    public Task<ProductsImportJob?> GetProductsImportJobAsync(Guid jobRunId, CancellationToken cancellationToken)
    {
        return Task.FromResult<ProductsImportJob?>(null);
    }

    public Task CreateProductsImportJobAsync(ProductsImportJob job, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<JobRun?> GetByIdWithConnectionAsync(Guid id, CancellationToken cancellationToken)
    {
        _runs.TryGetValue(id, out JobRun? run);

        return Task.FromResult(run);
    }

    public Task<JobStatus> GetStatusAsync(Guid id, CancellationToken cancellationToken)
    {
        return Task.FromResult(_runs[id].Status);
    }

    public Task<int?> GetExpectedBatchCountAsync(Guid id, CancellationToken cancellationToken)
    {
        _runs.TryGetValue(id, out JobRun? run);

        return Task.FromResult(run?.ExpectedBatchCount);
    }

    public Task<bool> BatchResultExistsAsync(Guid messageId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_batchResults.Any(result => result.MessageId == messageId));
    }

    public void AddBatchResult(JobRunBatchResult batchResult)
    {
        _batchResults.Add(batchResult);
    }

    public Task<JobRunBatchResultTotals> GetBatchResultTotalsAsync(
        Guid jobRunId, CancellationToken cancellationToken)
    {
        List<JobRunBatchResult> results = _batchResults
            .Where(result => result.JobRunId == jobRunId)
            .ToList();

        List<string> errors = results
            .Where(result => result.ErrorMessage != null)
            .Select(result => result.ErrorMessage!)
            .ToList();

        JobRunBatchResultTotals totals = new()
        {
            TotalProcessed = results.Sum(result => result.RecordsProcessed),
            TotalCreated = results.Sum(result => result.RecordsCreated),
            TotalUpdated = results.Sum(result => result.RecordsUpdated),
            TotalSkipped = results.Sum(result => result.RecordsSkipped),
            TotalFailed = results.Sum(result => result.RecordsFailed),
            CombinedErrors = errors.Count > 0 ? string.Join("; ", errors) : null,
        };

        return Task.FromResult(totals);
    }

    public Task<int> CountBatchResultsAsync(Guid jobRunId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_batchResults.Count(result => result.JobRunId == jobRunId));
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(0);
    }
}

internal sealed class FakeJobCheckpointRepository : IJobCheckpointRepository
{
    private readonly Dictionary<Guid, JobCheckpoint> _checkpoints = new();

    public Task<JobCheckpoint?> GetByJobRunIdAsync(Guid jobRunId, CancellationToken cancellationToken)
    {
        _checkpoints.TryGetValue(jobRunId, out JobCheckpoint? checkpoint);

        return Task.FromResult(checkpoint);
    }

    public Task UpsertAsync(JobCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        _checkpoints[checkpoint.JobRunId] = checkpoint;

        return Task.CompletedTask;
    }
}

internal sealed class FakeSyncCheckpointRepository : ISyncCheckpointRepository
{
    private readonly Dictionary<(Guid ConnectionId, JobType JobType), SyncCheckpoint> _checkpoints = new();

    public SyncCheckpoint? Find(Guid connectionId, JobType jobType)
    {
        _checkpoints.TryGetValue((connectionId, jobType), out SyncCheckpoint? checkpoint);

        return checkpoint;
    }

    public Task<SyncCheckpoint?> GetAsync(Guid connectionId, JobType jobType, CancellationToken cancellationToken)
    {
        return Task.FromResult(Find(connectionId, jobType));
    }

    public Task UpsertAsync(SyncCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        _checkpoints[(checkpoint.ConnectionId, checkpoint.JobType)] = checkpoint;

        return Task.CompletedTask;
    }
}

internal sealed class RecordingEventPublisher : IEventPublisher
{
    public List<FinanceEntriesBatchImportRequestedEvent> FinanceBatches { get; } = new();

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken) where TEvent : class
    {
        if (@event is FinanceEntriesBatchImportRequestedEvent batch)
        {
            FinanceBatches.Add(batch);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Finance source whose responses the test scripts one dispatch at a time. Records every request so
/// the test can assert which cursor the orchestrator resumed from.
/// </summary>
internal sealed class ScriptedFinanceSource : IChannelApiClient, IFinanceEntriesSource
{
    private readonly Queue<Func<FinanceEntriesRequest, FinanceEntriesPage>> _responses = new();

    public List<FinanceEntriesRequest> Requests { get; } = new();

    public string ChannelCode { get; set; } = "wildberries";

    public void RespondWith(Func<FinanceEntriesRequest, FinanceEntriesPage> response)
    {
        _responses.Enqueue(response);
    }

    public Task<FinanceEntriesPage> GetFinanceEntriesAsync(
        Guid connectionId,
        FinanceEntriesRequest request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"The scripted finance source received request #{Requests.Count} but has no response left.");
        }

        return Task.FromResult(_responses.Dequeue()(request));
    }

    public Task<AccountInfo?> GetAccountInfoAsync(
        Guid? connectionId,
        string apiKey,
        string? apiSecret,
        Dictionary<string, string>? settings,
        CancellationToken ct)
    {
        return Task.FromResult<AccountInfo?>(null);
    }

    public Task<ApiResponse<ProductCardsData>> GetProductCardsAsync(
        Guid connectionId, ProductCardsCursor? cursor, int limit, CancellationToken ct)
    {
        return Task.FromResult(new ApiResponse<ProductCardsData>());
    }
}

internal sealed class ScriptedChannelApiClientFactory : IChannelApiClientFactory
{
    public ScriptedChannelApiClientFactory(ScriptedFinanceSource source)
    {
        Source = source;
    }

    public ScriptedFinanceSource Source { get; }

    public IChannelApiClient Create(SystemChannelTemplate template)
    {
        Source.ChannelCode = template.Code;

        return Source;
    }
}

/// <summary>
/// Execution scope that records how often the orchestrator opened it, so a test can assert the
/// run's HTTP calls are attributable to the JobRun.
/// </summary>
internal sealed class RecordingJobRunExecutionScope : IJobRunExecutionScope
{
    public int Entered { get; private set; }

    public Guid? LastJobRunId { get; private set; }

    public IDisposable EnterScope(Guid jobRunId, Guid? connectionId = null, Guid? channelId = null)
    {
        Entered++;
        LastJobRunId = jobRunId;

        return new NoOpScope();
    }

    private sealed class NoOpScope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

internal sealed class FixedTenantTimeService : ITenantTimeService
{
    public Task<TimeZoneInfo> GetTimeZoneAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        return Task.FromResult(TimeZoneInfo.Utc);
    }

    public Task<DateOnly> GetTodayAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        return Task.FromResult(new DateOnly(2026, 11, 19));
    }
}

internal sealed class FixedTenantContext : ITenantContext
{
    public FixedTenantContext(Guid tenantId)
    {
        TenantId = tenantId;
    }

    public Guid? TenantId { get; }
}
