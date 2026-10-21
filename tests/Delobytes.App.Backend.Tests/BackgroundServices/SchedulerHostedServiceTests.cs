using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.BackgroundServices;
using Delobytes.App.Backend.Contracts.Accounting;
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
/// Tests SchedulerHostedService: per tick it must turn each due JobSchedule into a Pending
/// JobRun, respect the one-active-job-per-connection/type rule, and advance the schedule's
/// NextRunAt in the tenant's time zone. It publishes nothing itself — dispatch, and therefore
/// the parallelism limit, is the worker's job.
/// </summary>
[Collection("BackgroundServices")]
public partial class SchedulerHostedServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _connectionId = Guid.NewGuid();
    private readonly Mock<IJobScheduleRepository> _scheduleRepositoryMock = new Mock<IJobScheduleRepository>();
    private readonly Mock<IJobRunRepository> _jobRunRepositoryMock = new Mock<IJobRunRepository>();
    private readonly Mock<IConnectionRepository> _connectionRepositoryMock = new Mock<IConnectionRepository>();
    private readonly Mock<ITenantSettingsProvider> _tenantSettingsProviderMock = new Mock<ITenantSettingsProvider>();
    private readonly MessageTenantContext _tenantContext = new MessageTenantContext();

    public SchedulerHostedServiceTests()
    {
        _connectionRepositoryMock
            .Setup(r => r.FindByIdWithTemplateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildConnection());

        _tenantSettingsProviderMock
            .Setup(p => p.GetAccountingSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAccountingSettings { Currency = "RUB", TimeZoneId = "UTC" });

        _jobRunRepositoryMock
            .Setup(r => r.HasActiveJobAsync(It.IsAny<Guid>(), It.IsAny<JobType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _jobRunRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobRun jobRun, CancellationToken _) => jobRun);

        _scheduleRepositoryMock
            .Setup(r => r.GetDueSchedulesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JobSchedule>());

        _scheduleRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<JobSchedule>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // ── Tick: job creation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Tick_WhenNoSchedulesAreDue_CreatesNoJobRuns()
    {
        SchedulerHostedService scheduler = BuildScheduler();

        await RunSingleTickAsync(scheduler);

        _jobRunRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _scheduleRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<JobSchedule>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Tick_DueOrdersImportSchedule_CreatesPendingSystemJobRunAndTypeSpecificRow()
    {
        // Arrange
        JobSchedule schedule = BuildSchedule(JobType.OrdersImport);
        SetupDueSchedules(schedule);

        List<JobRun> createdRuns = new List<JobRun>();
        _jobRunRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .Callback((JobRun jobRun, CancellationToken _) => createdRuns.Add(jobRun))
            .ReturnsAsync((JobRun jobRun, CancellationToken _) => jobRun);

        SchedulerHostedService scheduler = BuildScheduler();

        // Act
        await RunSingleTickAsync(scheduler);

        // Assert
        createdRuns.Should().HaveCount(1);
        JobRun created = createdRuns[0];
        created.ConnectionId.Should().Be(_connectionId);
        created.JobType.Should().Be(JobType.OrdersImport);
        created.Status.Should().Be(JobStatus.Pending);
        created.RequestedByUserId.Should().BeNull("scheduler-initiated jobs are system-owned");
        created.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        _jobRunRepositoryMock.Verify(
            r => r.CreateOrdersImportJobAsync(
                It.Is<OrdersImportJob>(j => j.JobRunId == created.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _jobRunRepositoryMock.Verify(
            r => r.CreateProductsImportJobAsync(It.IsAny<ProductsImportJob>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Tick_DueProductsImportSchedule_CreatesProductsTypeSpecificRow()
    {
        JobSchedule schedule = BuildSchedule(JobType.ProductsImport);
        SetupDueSchedules(schedule);

        SchedulerHostedService scheduler = BuildScheduler();

        await RunSingleTickAsync(scheduler);

        _jobRunRepositoryMock.Verify(
            r => r.CreateProductsImportJobAsync(
                It.Is<ProductsImportJob>(j => j.JobRunId != Guid.Empty),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _jobRunRepositoryMock.Verify(
            r => r.CreateOrdersImportJobAsync(It.IsAny<OrdersImportJob>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Tick_UnsupportedJobType_CreatesJobRunWithoutTypeSpecificRow()
    {
        // OrdersSync has no scheduler support yet; the JobRun must still exist so the work
        // is tracked rather than silently dropped.
        JobSchedule schedule = BuildSchedule(JobType.OrdersSync);
        SetupDueSchedules(schedule);

        SchedulerHostedService scheduler = BuildScheduler();

        await RunSingleTickAsync(scheduler);

        _jobRunRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _jobRunRepositoryMock.Verify(
            r => r.CreateOrdersImportJobAsync(It.IsAny<OrdersImportJob>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _jobRunRepositoryMock.Verify(
            r => r.CreateProductsImportJobAsync(It.IsAny<ProductsImportJob>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Tick: one active job per (ConnectionId, JobType) ─────────────────────────────

    [Fact]
    public async Task Tick_WhenActiveJobExists_SkipsScheduleAndDoesNotAdvanceNextRunAt()
    {
        // The skipped schedule must stay due so the next tick re-evaluates it once the
        // in-flight job frees the slot.
        JobSchedule schedule = BuildSchedule(JobType.OrdersImport);
        SetupDueSchedules(schedule);

        _jobRunRepositoryMock
            .Setup(r => r.HasActiveJobAsync(_connectionId, JobType.OrdersImport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        DateTimeOffset? nextRunAtBeforeTick = schedule.NextRunAt;
        SchedulerHostedService scheduler = BuildScheduler();

        await RunSingleTickAsync(scheduler);

        _jobRunRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _scheduleRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<JobSchedule>(), It.IsAny<CancellationToken>()),
            Times.Never);
        schedule.NextRunAt.Should().Be(nextRunAtBeforeTick, "a skipped tick must leave the schedule due");
        schedule.LastRunAt.Should().BeNull();
    }

    // ── Tick: connection guards ─────────────────────────────────────────────────────

    [Fact]
    public async Task Tick_WhenConnectionIsInactive_SkipsJobRunButAdvancesSchedule()
    {
        // Advancing anyway keeps an inactive connection from wedging the scheduler on
        // every subsequent tick.
        Connection inactive = BuildConnection();
        inactive.IsActive = false;

        _connectionRepositoryMock
            .Setup(r => r.FindByIdWithTemplateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactive);

        JobSchedule schedule = BuildSchedule(JobType.OrdersImport);
        SetupDueSchedules(schedule);

        SchedulerHostedService scheduler = BuildScheduler();

        await RunSingleTickAsync(scheduler);

        _jobRunRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _scheduleRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<JobSchedule>(), It.IsAny<CancellationToken>()),
            Times.Once);
        schedule.NextRunAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Tick_WhenConnectionIsMissing_SkipsJobRunButAdvancesSchedule()
    {
        _connectionRepositoryMock
            .Setup(r => r.FindByIdWithTemplateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        JobSchedule schedule = BuildSchedule(JobType.OrdersImport);
        SetupDueSchedules(schedule);

        SchedulerHostedService scheduler = BuildScheduler();

        await RunSingleTickAsync(scheduler);

        _jobRunRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _scheduleRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<JobSchedule>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Tick: schedule advancement ──────────────────────────────────────────────────

    [Fact]
    public async Task Tick_AfterCreatingJobRun_AdvancesLastRunAtAndNextRunAt()
    {
        JobSchedule schedule = BuildSchedule(JobType.OrdersImport);
        SetupDueSchedules(schedule);

        DateTimeOffset tickStartedAt = DateTimeOffset.UtcNow;
        SchedulerHostedService scheduler = BuildScheduler();

        await RunSingleTickAsync(scheduler);

        schedule.LastRunAt.Should().NotBeNull();
        schedule.LastRunAt!.Value.Should().BeOnOrAfter(tickStartedAt.AddSeconds(-1));

        // "0 * * * *": the next boundary is within the coming hour of LastRunAt.
        schedule.NextRunAt.Should().NotBeNull();
        schedule.NextRunAt!.Value.Should().BeAfter(schedule.LastRunAt!.Value);
        schedule.NextRunAt!.Value.Should().BeBefore(schedule.LastRunAt!.Value.AddHours(1).AddMinutes(1));

        _scheduleRepositoryMock.Verify(
            r => r.UpdateAsync(schedule, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("Europe/Moscow")]
    [InlineData("Asia/Vladivostok")]
    public async Task Tick_AdvancesNextRunAt_ToTheNextHourlyBoundaryInTheTenantTimeZone(string timeZoneId)
    {
        _tenantSettingsProviderMock
            .Setup(p => p.GetAccountingSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAccountingSettings { Currency = "RUB", TimeZoneId = timeZoneId });

        JobSchedule schedule = BuildSchedule(JobType.OrdersImport);
        SetupDueSchedules(schedule);

        SchedulerHostedService scheduler = BuildScheduler();

        // Boundaries are sampled on both sides of the tick: if the test happens to straddle an
        // exact hour, the service may legitimately land on either one.
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        DateTimeOffset before = NextHourlyBoundary(timeZone, DateTimeOffset.UtcNow);

        await RunSingleTickAsync(scheduler);

        DateTimeOffset after = NextHourlyBoundary(timeZone, DateTimeOffset.UtcNow);

        _tenantSettingsProviderMock.Verify(
            p => p.GetAccountingSettingsAsync(_tenantId, It.IsAny<CancellationToken>()),
            Times.Once);

        schedule.NextRunAt.Should().NotBeNull();
        schedule.NextRunAt!.Value.Should().BeOneOf(new[] { before, after });
        schedule.NextRunAt.Value.Offset.Should().Be(TimeSpan.Zero, "NextRunAt is persisted in UTC");
    }

    [Fact]
    public async Task Tick_WhenTenantTimeZoneIsUnknown_FallsBackToUtcInsteadOfFailing()
    {
        _tenantSettingsProviderMock
            .Setup(p => p.GetAccountingSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAccountingSettings { Currency = "RUB", TimeZoneId = "Not/AZone" });

        JobSchedule schedule = BuildSchedule(JobType.OrdersImport);
        SetupDueSchedules(schedule);

        SchedulerHostedService scheduler = BuildScheduler();

        await RunSingleTickAsync(scheduler);

        // An unknown zone must not abort the tick: the schedule is still created and saved.
        schedule.NextRunAt.Should().NotBeNull();
        schedule.NextRunAt!.Value.Should().BeCloseTo(NextHourlyBoundary(TimeZoneInfo.Utc, DateTimeOffset.UtcNow), TimeSpan.FromMinutes(1));
        _scheduleRepositoryMock.Verify(
            r => r.UpdateAsync(schedule, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Tick_WhenTenantSettingsLookupThrows_FallsBackToUtc()
    {
        _tenantSettingsProviderMock
            .Setup(p => p.GetAccountingSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("tenant not found"));

        JobSchedule schedule = BuildSchedule(JobType.OrdersImport);
        SetupDueSchedules(schedule);

        SchedulerHostedService scheduler = BuildScheduler();

        await RunSingleTickAsync(scheduler);

        schedule.NextRunAt.Should().NotBeNull();
    }

    // ── Tick: fault isolation and tenant fan-out ────────────────────────────────────

    [Fact]
    public async Task Tick_OneScheduleFails_DoesNotPreventTheNextOneFromBeingProcessed()
    {
        JobSchedule failing = BuildSchedule(JobType.OrdersImport);
        JobSchedule succeeding = BuildSchedule(JobType.OrdersImport);
        SetupDueSchedules(failing, succeeding);

        _jobRunRepositoryMock
            .SetupSequence(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"))
            .ReturnsAsync(new JobRun());

        SchedulerHostedService scheduler = BuildScheduler();

        await RunSingleTickAsync(scheduler);

        _scheduleRepositoryMock.Verify(
            r => r.UpdateAsync(succeeding, It.IsAny<CancellationToken>()),
            Times.Once);

        // The failed schedule keeps its NextRunAt so it is retried on the next tick.
        failing.NextRunAt.Should().NotBeNull();
        failing.LastRunAt.Should().BeNull();
    }

    [Fact]
    public async Task Tick_DueSchedulesForDifferentTenants_CreatesAJobRunForEach()
    {
        Guid otherTenantId = Guid.NewGuid();
        Guid otherConnectionId = Guid.NewGuid();

        JobSchedule first = BuildSchedule(JobType.OrdersImport);
        JobSchedule second = BuildSchedule(JobType.OrdersImport);
        second.TenantId = otherTenantId;
        second.ConnectionId = otherConnectionId;

        // Each schedule carries its own TenantId because the scheduler resolves a fresh
        // per-schedule scope before touching any tenant-scoped repository.
        SetupDueSchedules(first, second);

        SchedulerHostedService scheduler = BuildScheduler();

        await RunSingleTickAsync(scheduler);

        _jobRunRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _jobRunRepositoryMock.Verify(
            r => r.CreateAsync(
                It.Is<JobRun>(j => j.ConnectionId == otherConnectionId),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // The tenant must be switched per schedule, not left at the first one's value:
        // the time zone lookup is keyed on the tenant and happens once per schedule.
        _tenantSettingsProviderMock.Verify(
            p => p.GetAccountingSettingsAsync(_tenantId, It.IsAny<CancellationToken>()),
            Times.Once);
        _tenantSettingsProviderMock.Verify(
            p => p.GetAccountingSettingsAsync(otherTenantId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenATickThrows_KeepsTicking()
    {
        // The loop must survive a failing tick; otherwise one bad query silently kills
        // scheduling for the lifetime of the pod.
        _scheduleRepositoryMock
            .SetupSequence(r => r.GetDueSchedulesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"))
            .ReturnsAsync(new List<JobSchedule> { BuildSchedule(JobType.OrdersImport) });

        SchedulerHostedService scheduler = BuildScheduler(tickIntervalSeconds: 1);

        await scheduler.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () => _jobRunRepositoryMock.Invocations.Any(i => i.Method.Name == nameof(IJobRunRepository.CreateAsync)),
            TimeSpan.FromSeconds(20));

        await scheduler.StopAsync(CancellationToken.None);

        _jobRunRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    private SchedulerHostedService BuildScheduler(int tickIntervalSeconds = 3600)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(_scheduleRepositoryMock.Object);
        services.AddSingleton(_jobRunRepositoryMock.Object);
        services.AddSingleton(_connectionRepositoryMock.Object);
        services.AddSingleton(_tenantSettingsProviderMock.Object);
        services.AddSingleton(_tenantContext);

        ServiceProvider provider = services.BuildServiceProvider();

        return new SchedulerHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new BackgroundJobsOptions { SchedulerTickIntervalSeconds = tickIntervalSeconds }),
            NullLogger<SchedulerHostedService>.Instance);
    }

    private void SetupDueSchedules(params JobSchedule[] schedules)
    {
        _scheduleRepositoryMock
            .Setup(r => r.GetDueSchedulesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedules.ToList());
    }

    private JobSchedule BuildSchedule(JobType jobType)
    {
        return new JobSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ConnectionId = _connectionId,
            JobType = jobType,
            CronExpression = "0 * * * *",
            IsEnabled = true,
            LastRunAt = null,
            NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };
    }

    private Connection BuildConnection()
    {
        return new Connection
        {
            Id = _connectionId,
            ChannelId = Guid.NewGuid(),
            SystemChannelTemplateId = Guid.NewGuid(),
            Name = "Test connection",
            ApiKey = "test-key",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Mirrors SchedulerHostedService.ComputeNextRunAt independently: the cron is evaluated in
    /// the tenant's local wall-clock time and the result is converted back to UTC.
    /// </summary>
    private static DateTimeOffset NextHourlyBoundary(TimeZoneInfo timeZone, DateTimeOffset fromUtc)
    {
        DateTime local = TimeZoneInfo.ConvertTime(fromUtc, timeZone).DateTime;
        DateTime truncated = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, DateTimeKind.Unspecified);
        DateTime nextLocal = truncated.AddHours(1);
        DateTime nextUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocal, timeZone);
        return new DateTimeOffset(nextUtc, TimeSpan.Zero);
    }

    /// <summary>
    /// Invokes one scheduler pass. TickAsync is private by design (the only public entry point
    /// is the PeriodicTimer loop, which would make every test wait a full interval), so the
    /// single-tick scenarios call it directly and the loop itself is covered separately by
    /// <see cref="ExecuteAsync_WhenATickThrows_KeepsTicking"/>.
    /// </summary>
    private static async Task RunSingleTickAsync(SchedulerHostedService scheduler)
    {
        MethodInfo? tickMethod = typeof(SchedulerHostedService)
            .GetMethod("TickAsync", BindingFlags.Instance | BindingFlags.NonPublic);

        tickMethod.Should().NotBeNull("SchedulerHostedService is expected to expose a private TickAsync");

        object? result = tickMethod!.Invoke(scheduler, new object[] { CancellationToken.None });
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
