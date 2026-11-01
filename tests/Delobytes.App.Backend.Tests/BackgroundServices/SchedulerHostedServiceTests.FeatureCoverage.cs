using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.BackgroundServices;
using Delobytes.App.Backend.Contracts.Accounting;
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
        JobSchedule schedule = BuildSchedule(JobType.OrdersImport);
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

        _jobRunRepositoryMock.Verify(r => r.CreateAsync(It.Is<JobRun>(j => j.JobType == JobType.OrdersImport && j.Status == JobStatus.Pending), It.IsAny<CancellationToken>()), Times.Once);
        schedule.NextRunAt!.Value.Should().BeAfter(originalNextRunAt!.Value);
    }

    [Fact]
    public async Task Tick_FutureWildberriesSlot_UsesWildberriesSupplierOrdersScope()
    {
        // The channel-neutral limiter must still be asked about the Wildberries orders bucket,
        // otherwise a fresh Ozon bucket (always empty) would let every tick through.
        JobSchedule schedule = BuildSchedule(JobType.OrdersImport);
        Connection connection = BuildConnection(OrdersJobTypeMap.WildberriesChannelCode);
        SetupDueSchedules(schedule);
        _connectionRepositoryMock.Setup(r => r.FindByIdWithTemplateAsync(_connectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        Mock<IOrdersRateLimiter> limiter = BuildOrdersRateLimiter();
        SchedulerHostedService scheduler = BuildGateScheduler(limiter);

        await RunSingleTickAsync(scheduler);

        limiter.Verify(
            l => l.GetNextRequestAllowedAtAsync(
                It.Is<RateLimitScope>(s =>
                    s.ChannelCode == OrdersJobTypeMap.WildberriesChannelCode
                    && s.ApiKey == connection.ApiKey
                    && s.Method == OrdersRateLimiterMethods.WildberriesSupplierOrders),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Tick_OzonOrdersImportFbo_UsesOzonPostingFboListScope()
    {
        JobSchedule schedule = BuildSchedule(JobType.OrdersImportFbo);
        Connection connection = BuildConnection(OrdersJobTypeMap.OzonChannelCode);
        SetupDueSchedules(schedule);
        _connectionRepositoryMock.Setup(r => r.FindByIdWithTemplateAsync(_connectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        Mock<IOrdersRateLimiter> limiter = BuildOrdersRateLimiter();
        SchedulerHostedService scheduler = BuildGateScheduler(limiter);

        await RunSingleTickAsync(scheduler);

        limiter.Verify(
            l => l.GetNextRequestAllowedAtAsync(
                It.Is<RateLimitScope>(s =>
                    s.ChannelCode == OrdersJobTypeMap.OzonChannelCode
                    && s.ApiKey == connection.ApiKey
                    && s.Method == OrdersRateLimiterMethods.OzonPostingFboList),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _jobRunRepositoryMock.Verify(
            r => r.CreateAsync(It.Is<JobRun>(j => j.JobType == JobType.OrdersImportFbo), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(JobType.ProductsImport, OrdersJobTypeMap.WildberriesChannelCode)]
    [InlineData(JobType.OrdersImport, "yandex.kit")]
    [InlineData(JobType.OrdersImport, "")]
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
        SetupDueSchedules(BuildSchedule(JobType.OrdersImport));
        _jobRunRepositoryMock.Setup(r => r.HasActiveJobAsync(_connectionId, JobType.OrdersImport, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Mock<IOrdersRateLimiter> limiter = new Mock<IOrdersRateLimiter>(MockBehavior.Strict);

        await RunSingleTickAsync(BuildGateScheduler(limiter));

        limiter.VerifyNoOtherCalls();
        _jobRunRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private SchedulerHostedService BuildGateScheduler(Mock<IOrdersRateLimiter> limiter)
    {
        return BuildScheduler(ordersRateLimiter: limiter.Object);
    }
}
