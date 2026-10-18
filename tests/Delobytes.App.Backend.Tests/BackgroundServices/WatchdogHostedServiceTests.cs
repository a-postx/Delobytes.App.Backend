using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.BackgroundServices;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;
using Delobytes.App.Backend.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.BackgroundServices;

/// <summary>
/// Tests WatchdogHostedService: on each tick it must fail every Running JobRun that stayed
/// Running past its own JobType timeout, apply each type's timeout independently, span all
/// tenants, and leave everything else untouched.
/// </summary>
[Collection("BackgroundServices")]
public class WatchdogHostedServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly Guid _connectionId = Guid.NewGuid();
    private readonly MessageTenantContext _tenantContext = new MessageTenantContext();
    private readonly Mock<IJobTypeConfigurationRepository> _jobTypeConfigurationRepositoryMock = new Mock<IJobTypeConfigurationRepository>();
    private readonly DbContextOptions<IntegrationsDbContext> _options;
    private readonly IntegrationsDbContext _context;

    public WatchdogHostedServiceTests()
    {
        _options = new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseInMemoryDatabase("watchdog-" + Guid.NewGuid())
            .Options;

        _context = new IntegrationsDbContext(_options, _tenantContext);

        // Default: no type has a configured timeout. Individual tests opt types in, which is
        // what makes the "unconfigured type is never timed out" case meaningful.
        _jobTypeConfigurationRepositoryMock
            .Setup(r => r.GetByTypeAsync(It.IsAny<JobType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobTypeConfiguration?)null);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    // ── Timeout detection ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tick_RunningJobPastItsTimeout_IsFailedWithTimeoutError()
    {
        ConfigureTimeout(JobType.OrdersImport, 30);
        JobRun stuck = await SeedJobRunAsync(JobType.OrdersImport, DateTimeOffset.UtcNow.AddMinutes(-90));

        WatchdogHostedService watchdog = BuildWatchdog(new JobRunRepository(_context));

        await RunSingleTickAsync(watchdog);

        JobRun reloaded = await ReadJobRunAsync(stuck.Id);

        reloaded.Status.Should().Be(JobStatus.Failed);
        reloaded.Error.Should().Be("Job exceeded timeout of 30 minutes");
        reloaded.CompletedAt.Should().NotBeNull("a failed job must carry its terminal timestamp");
        reloaded.CompletedAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Tick_RunningJobWithinItsTimeout_IsLeftRunning()
    {
        ConfigureTimeout(JobType.OrdersImport, 30);
        JobRun fresh = await SeedJobRunAsync(JobType.OrdersImport, DateTimeOffset.UtcNow.AddMinutes(-10));

        WatchdogHostedService watchdog = BuildWatchdog(new JobRunRepository(_context));

        await RunSingleTickAsync(watchdog);

        JobRun reloaded = await ReadJobRunAsync(fresh.Id);

        reloaded.Status.Should().Be(JobStatus.Running);
        reloaded.Error.Should().BeNull();
        reloaded.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Tick_AppliesEachJobTypesOwnTimeout_NotTheShortestGlobally()
    {
        // Both jobs started at the same moment. OrdersImport (30 min) must time out while
        // ProductsImport (60 min) must not: a single global threshold would catch both, and
        // a per-type loop that forgets to filter by type would fail the products import too.
        ConfigureTimeout(JobType.OrdersImport, 30);
        ConfigureTimeout(JobType.ProductsImport, 60);

        DateTimeOffset startedAt = DateTimeOffset.UtcNow.AddMinutes(-45);
        JobRun ordersJob = await SeedJobRunAsync(JobType.OrdersImport, startedAt);
        JobRun productsJob = await SeedJobRunAsync(JobType.ProductsImport, startedAt);

        WatchdogHostedService watchdog = BuildWatchdog(new JobRunRepository(_context));

        await RunSingleTickAsync(watchdog);

        (await ReadJobRunAsync(ordersJob.Id)).Status.Should().Be(JobStatus.Failed);
        (await ReadJobRunAsync(productsJob.Id)).Status.Should().Be(JobStatus.Running);
    }

    [Fact]
    public async Task Tick_JobTypeWithoutConfiguration_IsNeverTimedOut()
    {
        // OrdersSync has no seeded JobTypeConfiguration row; without a timeout policy the
        // watchdog must skip the type rather than fall back to some default.
        ConfigureTimeout(JobType.OrdersImport, 30);
        JobRun unconfigured = await SeedJobRunAsync(JobType.OrdersSync, DateTimeOffset.UtcNow.AddHours(-10));

        WatchdogHostedService watchdog = BuildWatchdog(new JobRunRepository(_context));

        await RunSingleTickAsync(watchdog);

        JobRun reloaded = await ReadJobRunAsync(unconfigured.Id);

        reloaded.Status.Should().Be(JobStatus.Running);
        reloaded.Error.Should().BeNull();
    }

    [Fact]
    public async Task Tick_TimesOutStuckJobsAcrossAllTenants()
    {
        // The whole point of the watchdog running in the host is that a crashed job of any
        // tenant gets reconciled; a tenant-filtered query would only ever see the caller's rows.
        ConfigureTimeout(JobType.OrdersImport, 30);

        JobRun ownTenantJob = await SeedJobRunAsync(JobType.OrdersImport, DateTimeOffset.UtcNow.AddMinutes(-90));
        JobRun foreignTenantJob = await SeedJobRunAsync(
            JobType.OrdersImport,
            DateTimeOffset.UtcNow.AddMinutes(-90),
            _otherTenantId);

        WatchdogHostedService watchdog = BuildWatchdog(new JobRunRepository(_context));

        await RunSingleTickAsync(watchdog);

        (await ReadJobRunAsync(ownTenantJob.Id)).Status.Should().Be(JobStatus.Failed);
        (await ReadJobRunAsync(foreignTenantJob.Id)).Status.Should().Be(JobStatus.Failed);
    }

    [Fact]
    public async Task Tick_JobRunNotInRunningState_IsNeverFetched()
    {
        // Pending/Success/Cancelled jobs are outside the watchdog's remit; only Running rows
        // are candidates for a timeout.
        ConfigureTimeout(JobType.OrdersImport, 30);
        JobRun completed = await SeedJobRunAsync(
            JobType.OrdersImport,
            DateTimeOffset.UtcNow.AddHours(-5),
            status: JobStatus.Success);

        WatchdogHostedService watchdog = BuildWatchdog(new JobRunRepository(_context));

        await RunSingleTickAsync(watchdog);

        JobRun reloaded = await ReadJobRunAsync(completed.Id);

        reloaded.Status.Should().Be(JobStatus.Success);
        reloaded.Error.Should().BeNull();
    }

    // ── Races and fault isolation ────────────────────────────────────────────────────────

    [Fact]
    public async Task Tick_JobCompletedBetweenLookupAndUpdate_IsNotOverwritten()
    {
        // The job may reach a terminal state while the watchdog is mid-tick. Re-reading before
        // writing is what prevents a finished job from being retroactively marked Failed.
        ConfigureTimeout(JobType.OrdersImport, 30);
        JobRun stuck = await SeedJobRunAsync(JobType.OrdersImport, DateTimeOffset.UtcNow.AddMinutes(-90));

        Mock<IJobRunRepository> repositoryMock = new Mock<IJobRunRepository>();
        repositoryMock
            .Setup(r => r.GetRunningJobsOlderThanAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JobRun> { stuck });
        repositoryMock
            .Setup(r => r.GetByIdAsync(stuck.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobRun
            {
                Id = stuck.Id,
                ConnectionId = stuck.ConnectionId,
                JobType = stuck.JobType,
                Status = JobStatus.Success,
                StartedAt = stuck.StartedAt,
                CreatedAt = stuck.CreatedAt,
            });

        WatchdogHostedService watchdog = BuildWatchdog(repositoryMock.Object);

        await RunSingleTickAsync(watchdog);

        repositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()),
            Times.Never);
        stuck.Status.Should().Be(JobStatus.Running);
    }

    [Fact]
    public async Task Tick_WhenOneJobFailsToLoad_ContinuesWithRemainingJobs()
    {
        // A single job that errors mid-tick (concurrent update, transient DB error) must not
        // abort the pass and strand every other stuck job until the next interval.
        ConfigureTimeout(JobType.OrdersImport, 30);

        JobRun failingJob = await SeedJobRunAsync(JobType.OrdersImport, DateTimeOffset.UtcNow.AddMinutes(-90));
        JobRun healthyJob = await SeedJobRunAsync(JobType.OrdersImport, DateTimeOffset.UtcNow.AddMinutes(-90));

        Mock<IJobRunRepository> repositoryMock = new Mock<IJobRunRepository>();
        repositoryMock
            .Setup(r => r.GetRunningJobsOlderThanAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JobRun> { failingJob, healthyJob });
        repositoryMock
            .Setup(r => r.GetByIdAsync(failingJob.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("concurrent update"));
        repositoryMock
            .Setup(r => r.GetByIdAsync(healthyJob.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(healthyJob);
        repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        WatchdogHostedService watchdog = BuildWatchdog(repositoryMock.Object);

        await RunSingleTickAsync(watchdog);

        healthyJob.Status.Should().Be(JobStatus.Failed, "one bad row must not abort the whole tick");
        failingJob.Status.Should().Be(JobStatus.Running);
    }

    [Fact]
    public async Task Tick_WhenNothingIsStuck_PerformsNoWrites()
    {
        ConfigureTimeout(JobType.OrdersImport, 30);

        Mock<IJobRunRepository> repositoryMock = new Mock<IJobRunRepository>();
        repositoryMock
            .Setup(r => r.GetRunningJobsOlderThanAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JobRun>());

        WatchdogHostedService watchdog = BuildWatchdog(repositoryMock.Object);

        await RunSingleTickAsync(watchdog);

        repositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()),
            Times.Never);
        repositoryMock.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Loop resilience ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenATickThrows_KeepsTicking()
    {
        // PeriodicTimer does not fire on start: the first tick lands one full period in, the
        // second one period later. The interval is sub-second here (0.01 min = 0.6 s) instead of
        // the production 5 minutes, which keeps the test far under any CI budget while still
        // exercising the real loop through StartAsync.
        int invocationCount = 0;

        _jobTypeConfigurationRepositoryMock
            .Setup(r => r.GetByTypeAsync(It.IsAny<JobType>(), It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref invocationCount))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        Mock<IJobRunRepository> repositoryMock = new Mock<IJobRunRepository>();
        repositoryMock
            .Setup(r => r.GetRunningJobsOlderThanAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JobRun>());

        WatchdogHostedService watchdog = BuildWatchdog(repositoryMock.Object, intervalMinutes: 0.01);

        await watchdog.StartAsync(CancellationToken.None);

        // Two failed passes: the exception from the first must have been swallowed by the loop
        // rather than terminating the BackgroundService.
        await WaitUntilAsync(
            () => Volatile.Read(ref invocationCount) > 1,
            TimeSpan.FromSeconds(30));

        await watchdog.StopAsync(CancellationToken.None);

        Volatile.Read(ref invocationCount).Should().BeGreaterThan(
            1,
            "the service must survive a failed tick instead of terminating");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetInterval_WhenConfiguredIntervalIsNotPositive_FallsBackToFiveMinutes()
    {
        // PeriodicTimer throws on a non-positive period, which would take the host down at
        // startup. The fallback is part of the contract, so it is exercised directly: waiting
        // for the real five-minute period is impossible.
        MethodInfo? getIntervalMethod = typeof(WatchdogHostedService)
            .GetMethod("GetInterval", BindingFlags.Instance | BindingFlags.NonPublic);

        getIntervalMethod.Should().NotBeNull("WatchdogHostedService is expected to expose a private GetInterval");

        WatchdogHostedService zeroInterval = BuildWatchdog(new JobRunRepository(_context), intervalMinutes: 0);
        WatchdogHostedService negativeInterval = BuildWatchdog(new JobRunRepository(_context), intervalMinutes: -1);
        WatchdogHostedService fractionalInterval = BuildWatchdog(new JobRunRepository(_context), intervalMinutes: 0.5);

        ((TimeSpan)getIntervalMethod!.Invoke(zeroInterval, Array.Empty<object>())!)
            .Should().Be(TimeSpan.FromMinutes(5));
        ((TimeSpan)getIntervalMethod.Invoke(negativeInterval, Array.Empty<object>())!)
            .Should().Be(TimeSpan.FromMinutes(5));
        ((TimeSpan)getIntervalMethod.Invoke(fractionalInterval, Array.Empty<object>())!)
            .Should().Be(TimeSpan.FromSeconds(30), "fractional minutes exist so tests can tick quickly");
    }

    private void ConfigureTimeout(JobType jobType, int timeoutMinutes)
    {
        _jobTypeConfigurationRepositoryMock
            .Setup(r => r.GetByTypeAsync(jobType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobTypeConfiguration
            {
                JobType = jobType,
                TimeoutMinutes = timeoutMinutes,
                BatchSize = 50,
            });
    }

    private async Task<JobRun> SeedJobRunAsync(
        JobType jobType,
        DateTimeOffset startedAt,
        Guid? tenantId = null,
        JobStatus status = JobStatus.Running)
    {
        JobRun jobRun = new JobRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = _connectionId,
            JobType = jobType,
            Status = status,
            RequestedByUserId = null,
            CreatedAt = startedAt,
            StartedAt = status == JobStatus.Pending ? null : startedAt,
        };

        _tenantContext.SetTenantId(tenantId ?? _tenantId);

        try
        {
            _context.JobRuns.Add(jobRun);
            await _context.SaveChangesAsync();
        }
        finally
        {
            _tenantContext.Clear();
        }

        return jobRun;
    }

    // Read-back goes through a separate context so the assertion reflects persisted state
    // rather than the singleton context's (possibly stale) tracked instances.
    private async Task<JobRun> ReadJobRunAsync(Guid id)
    {
        Mock<ITenantContext> noTenantMock = new Mock<ITenantContext>();
        noTenantMock.Setup(t => t.TenantId).Returns((Guid?)null);

        using IntegrationsDbContext reader = new IntegrationsDbContext(_options, noTenantMock.Object);

        return await reader.JobRuns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(j => j.Id == id);
    }

    private WatchdogHostedService BuildWatchdog(IJobRunRepository jobRunRepository, double intervalMinutes = 5)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(_tenantContext);
        services.AddSingleton<ITenantContext>(_tenantContext);
        services.AddSingleton(_context);
        services.AddSingleton(jobRunRepository);
        services.AddSingleton(_jobTypeConfigurationRepositoryMock.Object);

        ServiceProvider provider = services.BuildServiceProvider();

        return new WatchdogHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new WatchdogOptions { IntervalMinutes = intervalMinutes }),
            NullLogger<WatchdogHostedService>.Instance);
    }

    /// <summary>
    /// Invokes one watchdog pass. TickAsync is private by design (the public entry point is the
    /// timer loop, which would make every test wait a full interval), so single-tick scenarios
    /// call it directly while the loop itself is covered by
    /// <see cref="ExecuteAsync_WhenATickThrows_KeepsTicking"/>.
    /// </summary>
    private static async Task RunSingleTickAsync(WatchdogHostedService watchdog)
    {
        MethodInfo? tickMethod = typeof(WatchdogHostedService)
            .GetMethod("TickAsync", BindingFlags.Instance | BindingFlags.NonPublic);

        tickMethod.Should().NotBeNull("WatchdogHostedService is expected to expose a private TickAsync");

        object? result = tickMethod!.Invoke(watchdog, new object[] { CancellationToken.None });
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
