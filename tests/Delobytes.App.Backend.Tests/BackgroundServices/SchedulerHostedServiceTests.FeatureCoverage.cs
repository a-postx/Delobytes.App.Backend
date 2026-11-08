using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.BackgroundServices;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Integrations.Application.SalesChannels;
using Delobytes.App.Backend.Integrations.Application.Extensions;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
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

public partial class SchedulerHostedServiceTests
{
    [Theory]
    [InlineData("wildberries")]
    [InlineData("WiLdBeRrIeS")]
    public async Task Tick_FutureWildberriesSlot_KeepsScheduleDueThenCreatesRun(string code)
    {
        JobSchedule schedule = BuildSchedule(JobType.WbOrdersImport);
        Connection connection = BuildConnection();
        connection.SystemChannelTemplate = new SystemChannelTemplate { Id = connection.SystemChannelTemplateId, Code = code, DisplayName = code, ApiBaseUrl = "https://example.test", ApiVersion = "v3" };
        DateTimeOffset? originalNextRunAt = schedule.NextRunAt;
        SetupDueSchedules(schedule);
        _connectionRepositoryMock.Setup(r => r.FindByIdWithTemplateAsync(_connectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        Mock<IOrdersRateLimiter> limiter = new Mock<IOrdersRateLimiter>();
        limiter.SetupSequence(r => r.GetNextRequestAllowedAtAsync(It.IsAny<RateLimitScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTimeOffset.UtcNow.AddHours(3))
            .ReturnsAsync(DateTimeOffset.MinValue);
        SchedulerHostedService scheduler = BuildGateScheduler(limiter);

        await RunSingleTickAsync(scheduler);

        schedule.NextRunAt.Should().Be(originalNextRunAt);
        schedule.LastRunAt.Should().BeNull();
        _jobRunRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _scheduleRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<JobSchedule>(), It.IsAny<CancellationToken>()), Times.Never);

        await RunSingleTickAsync(scheduler);

        _jobRunRepositoryMock.Verify(r => r.CreateAsync(It.Is<JobRun>(j => j.JobType == JobType.WbOrdersImport && j.Status == JobStatus.Pending), It.IsAny<CancellationToken>()), Times.Once);
        schedule.NextRunAt!.Value.Should().BeAfter(originalNextRunAt!.Value);
    }

    [Fact]
    public async Task Tick_FutureWildberriesSlot_UsesWildberriesSupplierOrdersScope()
    {
        // The channel-neutral limiter must still be asked about the Wildberries orders bucket,
        // otherwise a fresh Ozon bucket (always empty) would let every tick through.
        JobSchedule schedule = BuildSchedule(JobType.WbOrdersImport);
        Connection connection = BuildConnection(ChannelCatalog.WildberriesCode);
        SetupDueSchedules(schedule);
        _connectionRepositoryMock.Setup(r => r.FindByIdWithTemplateAsync(_connectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        Mock<IOrdersRateLimiter> limiter = BuildOrdersRateLimiter();
        SchedulerHostedService scheduler = BuildGateScheduler(limiter);

        await RunSingleTickAsync(scheduler);

        limiter.Verify(
            l => l.GetNextRequestAllowedAtAsync(
                It.Is<RateLimitScope>(s =>
                    s.ChannelCode == ChannelCatalog.WildberriesCode
                    && s.ApiKey == connection.ApiKey
                    && s.Method == OrdersRateLimiterMethods.WildberriesSupplierOrders),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Tick_OzonOrdersImportFbo_UsesOzonPostingFboListScope()
    {
        JobSchedule schedule = BuildSchedule(JobType.OzonOrdersImportFbo);
        Connection connection = BuildConnection(ChannelCatalog.OzonCode);
        SetupDueSchedules(schedule);
        _connectionRepositoryMock.Setup(r => r.FindByIdWithTemplateAsync(_connectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        Mock<IOrdersRateLimiter> limiter = BuildOrdersRateLimiter();
        SchedulerHostedService scheduler = BuildGateScheduler(limiter);

        await RunSingleTickAsync(scheduler);

        limiter.Verify(
            l => l.GetNextRequestAllowedAtAsync(
                It.Is<RateLimitScope>(s =>
                    s.ChannelCode == ChannelCatalog.OzonCode
                    && s.ApiKey == connection.ApiKey
                    && s.Method == OrdersRateLimiterMethods.OzonPostingFboList),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _jobRunRepositoryMock.Verify(
            r => r.CreateAsync(It.Is<JobRun>(j => j.JobType == JobType.OzonOrdersImportFbo), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(JobType.ProductsImport, ChannelCatalog.WildberriesCode)]
    [InlineData(JobType.WbOrdersImport, "yandex.kit")]
    [InlineData(JobType.WbOrdersImport, "")]
    public async Task Tick_JobTypeNotMappedToChannel_DoesNotConsultLimiter(JobType type, string code)
    {
        // ProductsImport is paced by nothing here; an orders type on a channel without an orders
        // entry (or with no channel at all) is skipped before the limiter is ever resolved.
        Connection connection = BuildConnection();
        connection.SystemChannelTemplate = new SystemChannelTemplate { Id = connection.SystemChannelTemplateId, Code = code, DisplayName = code, ApiBaseUrl = "https://example.test", ApiVersion = "v3" };
        _connectionRepositoryMock.Setup(r => r.FindByIdWithTemplateAsync(_connectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        SetupDueSchedules(BuildSchedule(type));
        Mock<IOrdersRateLimiter> limiter = new Mock<IOrdersRateLimiter>(MockBehavior.Strict);

        await RunSingleTickAsync(BuildGateScheduler(limiter));

        limiter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Tick_ActiveJob_DoesNotConsultLimiter()
    {
        SetupDueSchedules(BuildSchedule(JobType.WbOrdersImport));
        _jobRunRepositoryMock.Setup(r => r.HasActiveJobAsync(_connectionId, JobType.WbOrdersImport, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Mock<IOrdersRateLimiter> limiter = new Mock<IOrdersRateLimiter>(MockBehavior.Strict);

        await RunSingleTickAsync(BuildGateScheduler(limiter));

        limiter.VerifyNoOtherCalls();
        _jobRunRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private SchedulerHostedService BuildGateScheduler(Mock<IOrdersRateLimiter> limiter)
    {
        return BuildScheduler(ordersRateLimiter: limiter.Object);
    }

    // ── Sequential Ozon execution and tenant parallelism ────────────────────────────────────────

    [Theory]
    [InlineData(JobType.OzonOrdersImportFbs)]
    [InlineData(JobType.OzonOrdersImportFbo)]
    public async Task Tick_AnotherOzonOrdersJobIsActive_SkipsScheduleAndDoesNotAdvanceNextRunAt(JobType scheduleJobType)
    {
        // FBO and FBS share one connection and one rate-limiter bucket: whichever fires second has to
        // wait, and its schedule must stay due so the next tick picks it up once the first finishes.
        _connectionRepositoryMock
            .Setup(r => r.FindByIdWithTemplateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildConnection(ChannelCatalog.OzonCode));

        JobSchedule schedule = BuildSchedule(scheduleJobType);
        SetupDueSchedules(schedule);
        DateTimeOffset? nextRunAtBeforeTick = schedule.NextRunAt;

        _jobRunRepositoryMock
            .Setup(r => r.HasActiveOzonOrdersJobAsync(_connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

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

    [Fact]
    public async Task Tick_OzonSchedule_UsesTheCrossSchemaGate_NotThePerTypeOne()
    {
        // Regression guard: falling back to HasActiveJobAsync would let FBS start while FBO runs,
        // because each type only ever sees its own active row.
        _connectionRepositoryMock
            .Setup(r => r.FindByIdWithTemplateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildConnection(ChannelCatalog.OzonCode));

        SetupDueSchedules(BuildSchedule(JobType.OzonOrdersImportFbs));

        _jobRunRepositoryMock
            .Setup(r => r.HasActiveJobAsync(It.IsAny<Guid>(), It.IsAny<JobType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        SchedulerHostedService scheduler = BuildScheduler();

        await RunSingleTickAsync(scheduler);

        _jobRunRepositoryMock.Verify(
            r => r.HasActiveOzonOrdersJobAsync(_connectionId, It.IsAny<CancellationToken>()),
            Times.Once);
        _jobRunRepositoryMock.Verify(
            r => r.CreateAsync(It.Is<JobRun>(j => j.JobType == JobType.OzonOrdersImportFbs), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Tick_WildberriesSchedule_DoesNotConsultTheOzonCrossSchemaGate()
    {
        SetupDueSchedules(BuildSchedule(JobType.WbOrdersImport));

        SchedulerHostedService scheduler = BuildScheduler();

        await RunSingleTickAsync(scheduler);

        _jobRunRepositoryMock.Verify(
            r => r.HasActiveOzonOrdersJobAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _jobRunRepositoryMock.Verify(
            r => r.HasActiveJobAsync(_connectionId, JobType.WbOrdersImport, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Tick_TenantParallelismLimitReached_SkipsScheduleAndDoesNotAdvanceNextRunAt(int parallelism)
    {
        _connectionRepositoryMock
            .Setup(r => r.FindByIdWithTemplateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildConnection(ChannelCatalog.OzonCode));

        JobSchedule schedule = BuildSchedule(JobType.OzonOrdersImportFbo);
        SetupDueSchedules(schedule);
        DateTimeOffset? nextRunAtBeforeTick = schedule.NextRunAt;

        _tenantSettingsProviderMock
            .Setup(p => p.GetMaxBackgroundJobsParallelismAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parallelism);
        _jobRunRepositoryMock
            .Setup(r => r.CountRunningJobsAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parallelism);

        SchedulerHostedService scheduler = BuildScheduler();

        await RunSingleTickAsync(scheduler);

        _jobRunRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "creating a Pending run while the tenant slot is full only hides why the schedule did not fire");
        _scheduleRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<JobSchedule>(), It.IsAny<CancellationToken>()),
            Times.Never);
        schedule.NextRunAt.Should().Be(nextRunAtBeforeTick);
    }

    [Fact]
    public async Task Tick_TenantParallelismLimitReached_SkipsBeforeConsultingTheRateLimiter()
    {
        // The tenant-wide gate is cheaper and stricter than the rate-limiter pre-check, so a full
        // tenant must not even reach the limiter.
        SetupDueSchedules(BuildSchedule(JobType.WbOrdersImport));

        _tenantSettingsProviderMock
            .Setup(p => p.GetMaxBackgroundJobsParallelismAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _jobRunRepositoryMock
            .Setup(r => r.CountRunningJobsAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        Mock<IOrdersRateLimiter> limiter = new Mock<IOrdersRateLimiter>(MockBehavior.Strict);

        await RunSingleTickAsync(BuildGateScheduler(limiter));

        limiter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Tick_RunningCountBelowLimit_CreatesTheJobRun()
    {
        _connectionRepositoryMock
            .Setup(r => r.FindByIdWithTemplateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildConnection(ChannelCatalog.OzonCode));

        SetupDueSchedules(BuildSchedule(JobType.OzonOrdersImportFbo));

        _tenantSettingsProviderMock
            .Setup(p => p.GetMaxBackgroundJobsParallelismAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _jobRunRepositoryMock
            .Setup(r => r.CountRunningJobsAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        SchedulerHostedService scheduler = BuildScheduler();

        await RunSingleTickAsync(scheduler);

        _jobRunRepositoryMock.Verify(
            r => r.CreateAsync(It.Is<JobRun>(j => j.JobType == JobType.OzonOrdersImportFbo), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
