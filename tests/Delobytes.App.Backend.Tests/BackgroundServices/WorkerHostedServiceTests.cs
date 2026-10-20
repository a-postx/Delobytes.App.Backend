using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.BackgroundServices;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.BackgroundServices;

/// <summary>
/// Tests WorkerHostedService: it fans out over tenants, dispatches at most
/// (Tenant.MaxBackgroundJobsParallelism − Running) Pending JobRuns per tenant, and turns each
/// dispatch into the internal event that actually starts the orchestration consumer.
/// </summary>
[Collection("BackgroundServices")]
public class WorkerHostedServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _connectionId = Guid.NewGuid();
    private readonly Mock<ITenantRepository> _tenantRepositoryMock = new Mock<ITenantRepository>();
    private readonly Mock<IJobRunRepository> _jobRunRepositoryMock = new Mock<IJobRunRepository>();
    private readonly Mock<ITenantSettingsProvider> _tenantSettingsProviderMock = new Mock<ITenantSettingsProvider>();
    private readonly Mock<IEventPublisher> _eventPublisherMock = new Mock<IEventPublisher>();
    private readonly MessageTenantContext _tenantContext = new MessageTenantContext();

    public WorkerHostedServiceTests()
    {
        _tenantRepositoryMock
            .Setup(r => r.GetAllActiveTenantIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { _tenantId });

        _tenantSettingsProviderMock
            .Setup(p => p.GetMaxBackgroundJobsParallelismAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _jobRunRepositoryMock
            .Setup(r => r.CountRunningJobsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _jobRunRepositoryMock
            .Setup(r => r.GetPendingJobsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JobRun>());

        _eventPublisherMock
            .Setup(p => p.PublishAsync(It.IsAny<It.IsAnyType>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // ── Dispatch ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tick_WhenNoActiveTenants_DoesNothing()
    {
        _tenantRepositoryMock
            .Setup(r => r.GetAllActiveTenantIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        WorkerHostedService worker = BuildWorker();

        await RunSingleTickAsync(worker);

        _jobRunRepositoryMock.Verify(
            r => r.GetPendingJobsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _eventPublisherMock.Verify(
            p => p.PublishAsync(It.IsAny<It.IsAnyType>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Tick_WhenNoJobsArePending_PublishesNothing()
    {
        WorkerHostedService worker = BuildWorker();

        await RunSingleTickAsync(worker);

        _eventPublisherMock.Verify(
            p => p.PublishAsync(It.IsAny<It.IsAnyType>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Tick_PendingProductsImport_PublishesProductsImportRequestedEvent()
    {
        JobRun pending = BuildJobRun(JobType.ProductsImport);
        SetupPending(pending);

        WorkerHostedService worker = BuildWorker();

        await RunSingleTickAsync(worker);

        _eventPublisherMock.Verify(
            p => p.PublishAsync(
                It.Is<ProductsImportRequestedEvent>(e =>
                    e.JobRunId == pending.Id && e.ConnectionId == _connectionId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Tick_PendingOrdersImport_PublishesOrdersImportRequestedEvent()
    {
        JobRun pending = BuildJobRun(JobType.OrdersImport);
        SetupPending(pending);

        WorkerHostedService worker = BuildWorker();

        await RunSingleTickAsync(worker);

        _eventPublisherMock.Verify(
            p => p.PublishAsync(
                It.Is<OrdersImportRequestedEvent>(e =>
                    e.JobRunId == pending.Id && e.ConnectionId == _connectionId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Tick_UnsupportedJobType_PublishesNothing()
    {
        // OrdersSync has no consumer yet; publishing an event nobody handles would strand
        // the job in Pending with no trace of why.
        JobRun pending = BuildJobRun(JobType.OrdersSync);
        SetupPending(pending);

        WorkerHostedService worker = BuildWorker();

        await RunSingleTickAsync(worker);

        _eventPublisherMock.Verify(
            p => p.PublishAsync(It.IsAny<It.IsAnyType>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Parallelism ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tick_RequestsOnlyAvailableSlots_AsThePendingLimit()
    {
        // parallelism = 3, running = 1 → at most 2 more may start.
        _tenantSettingsProviderMock
            .Setup(p => p.GetMaxBackgroundJobsParallelismAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        _jobRunRepositoryMock
            .Setup(r => r.CountRunningJobsAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        WorkerHostedService worker = BuildWorker();

        await RunSingleTickAsync(worker);

        _jobRunRepositoryMock.Verify(
            r => r.GetPendingJobsAsync(_tenantId, 2, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Tick_WhenRunningJobsFillTheLimit_DoesNotQueryPendingJobs()
    {
        _jobRunRepositoryMock
            .Setup(r => r.CountRunningJobsAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        WorkerHostedService worker = BuildWorker();

        await RunSingleTickAsync(worker);

        _jobRunRepositoryMock.Verify(
            r => r.GetPendingJobsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _eventPublisherMock.Verify(
            p => p.PublishAsync(It.IsAny<It.IsAnyType>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Tick_WhenParallelismIsZero_SkipsTheTenantEntirely()
    {
        _tenantSettingsProviderMock
            .Setup(p => p.GetMaxBackgroundJobsParallelismAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        WorkerHostedService worker = BuildWorker();

        await RunSingleTickAsync(worker);

        _jobRunRepositoryMock.Verify(
            r => r.CountRunningJobsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _jobRunRepositoryMock.Verify(
            r => r.GetPendingJobsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Tick_WhenParallelismIsNegative_SkipsTheTenantEntirely()
    {
        // A mis-seeded negative value must not be read as "unlimited".
        _tenantSettingsProviderMock
            .Setup(p => p.GetMaxBackgroundJobsParallelismAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(-1);

        WorkerHostedService worker = BuildWorker();

        await RunSingleTickAsync(worker);

        _jobRunRepositoryMock.Verify(
            r => r.GetPendingJobsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Tick_DispatchesEveryPendingJobReturnedWithinTheLimit()
    {
        JobRun first = BuildJobRun(JobType.OrdersImport);
        JobRun second = BuildJobRun(JobType.ProductsImport);
        SetupPending(first, second);
        _tenantSettingsProviderMock
            .Setup(p => p.GetMaxBackgroundJobsParallelismAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        WorkerHostedService worker = BuildWorker();

        await RunSingleTickAsync(worker);

        _eventPublisherMock.Verify(
            p => p.PublishAsync(It.IsAny<It.IsAnyType>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // ── Tenant fan-out and fault isolation ──────────────────────────────────────────

    [Fact]
    public async Task Tick_MultipleTenants_DispatchUsingEachTenantsOwnParallelism()
    {
        Guid tenantAId = Guid.NewGuid();
        Guid tenantBId = Guid.NewGuid();

        _tenantRepositoryMock
            .Setup(r => r.GetAllActiveTenantIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { tenantAId, tenantBId });

        _tenantSettingsProviderMock
            .Setup(p => p.GetMaxBackgroundJobsParallelismAsync(tenantAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);
        _tenantSettingsProviderMock
            .Setup(p => p.GetMaxBackgroundJobsParallelismAsync(tenantBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        WorkerHostedService worker = BuildWorker();

        await RunSingleTickAsync(worker);

        _jobRunRepositoryMock.Verify(
            r => r.GetPendingJobsAsync(tenantAId, 5, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _jobRunRepositoryMock.Verify(
            r => r.GetPendingJobsAsync(tenantBId, 2, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Tick_OneTenantFails_DoesNotPreventTheOtherFromBeingDispatched()
    {
        Guid failingTenantId = Guid.NewGuid();
        Guid healthyTenantId = Guid.NewGuid();

        _tenantRepositoryMock
            .Setup(r => r.GetAllActiveTenantIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { failingTenantId, healthyTenantId });

        _tenantSettingsProviderMock
            .Setup(p => p.GetMaxBackgroundJobsParallelismAsync(failingTenantId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("tenant not found"));
        _tenantSettingsProviderMock
            .Setup(p => p.GetMaxBackgroundJobsParallelismAsync(healthyTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        JobRun pending = BuildJobRun(JobType.OrdersImport);
        _jobRunRepositoryMock
            .Setup(r => r.GetPendingJobsAsync(healthyTenantId, It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JobRun> { pending });

        WorkerHostedService worker = BuildWorker();

        await RunSingleTickAsync(worker);

        _eventPublisherMock.Verify(
            p => p.PublishAsync(
                It.Is<OrdersImportRequestedEvent>(e => e.JobRunId == pending.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Tick_WhenPublishingFailsForOneJob_StillAttemptsTheNextTenant()
    {
        Guid tenantAId = Guid.NewGuid();
        Guid tenantBId = Guid.NewGuid();

        _tenantRepositoryMock
            .Setup(r => r.GetAllActiveTenantIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { tenantAId, tenantBId });

        _tenantSettingsProviderMock
            .Setup(p => p.GetMaxBackgroundJobsParallelismAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        JobRun tenantAJob = BuildJobRun(JobType.OrdersImport);
        JobRun tenantBJob = BuildJobRun(JobType.OrdersImport);

        _jobRunRepositoryMock
            .Setup(r => r.GetPendingJobsAsync(tenantAId, It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JobRun> { tenantAJob });
        _jobRunRepositoryMock
            .Setup(r => r.GetPendingJobsAsync(tenantBId, It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JobRun> { tenantBJob });

        _eventPublisherMock
            .SetupSequence(p => p.PublishAsync(It.IsAny<It.IsAnyType>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker unavailable"))
            .Returns(Task.CompletedTask);

        WorkerHostedService worker = BuildWorker();

        await RunSingleTickAsync(worker);

        _eventPublisherMock.Verify(
            p => p.PublishAsync(
                It.Is<OrdersImportRequestedEvent>(e => e.JobRunId == tenantBJob.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenATickThrows_KeepsTicking()
    {
        _tenantRepositoryMock
            .SetupSequence(r => r.GetAllActiveTenantIdsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"))
            .ReturnsAsync(new List<Guid> { _tenantId });

        WorkerHostedService worker = BuildWorker(tickIntervalSeconds: 1);

        await worker.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () => _jobRunRepositoryMock.Invocations.Any(i => i.Method.Name == nameof(IJobRunRepository.GetPendingJobsAsync)),
            TimeSpan.FromSeconds(20));

        await worker.StopAsync(CancellationToken.None);

        _jobRunRepositoryMock.Verify(
            r => r.GetPendingJobsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    private WorkerHostedService BuildWorker(int tickIntervalSeconds = 3600)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(_tenantRepositoryMock.Object);
        services.AddSingleton(_jobRunRepositoryMock.Object);
        services.AddSingleton(_tenantSettingsProviderMock.Object);
        services.AddSingleton(_eventPublisherMock.Object);
        services.AddSingleton(_tenantContext);

        ServiceProvider provider = services.BuildServiceProvider();

        return new WorkerHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new BackgroundJobsOptions { WorkerTickIntervalSeconds = tickIntervalSeconds }),
            NullLogger<WorkerHostedService>.Instance);
    }

    private void SetupPending(params JobRun[] jobs)
    {
        _jobRunRepositoryMock
            .Setup(r => r.GetPendingJobsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs.ToList());
    }

    private JobRun BuildJobRun(JobType jobType)
    {
        return new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = _connectionId,
            JobType = jobType,
            Status = JobStatus.Pending,
            RequestedByUserId = null,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Invokes one worker pass. TickAsync is private by design (the public entry point is the
    /// PeriodicTimer loop, which would make every test wait a full interval), so single-tick
    /// scenarios call it directly while the loop itself is covered by
    /// <see cref="ExecuteAsync_WhenATickThrows_KeepsTicking"/>.
    /// </summary>
    private static async Task RunSingleTickAsync(WorkerHostedService worker)
    {
        MethodInfo? tickMethod = typeof(WorkerHostedService)
            .GetMethod("TickAsync", BindingFlags.Instance | BindingFlags.NonPublic);

        tickMethod.Should().NotBeNull("WorkerHostedService is expected to expose a private TickAsync");

        object? result = tickMethod!.Invoke(worker, new object[] { CancellationToken.None });
        await (Task)result!;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }
    }
}
