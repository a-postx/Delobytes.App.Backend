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
        Mock<IWildberriesRateLimiter> limiter = new Mock<IWildberriesRateLimiter>();
        limiter.SetupSequence(r => r.GetNextRequestAllowedAtAsync(connection.ApiKey, WildberriesApiMethod.SupplierOrders, It.IsAny<CancellationToken>()))
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

    [Theory]
    [InlineData(JobType.ProductsImport, "wildberries")]
    [InlineData(JobType.OrdersImport, "ozon")]
    public async Task Tick_OutsideWildberriesOrders_DoesNotConsultLimiter(JobType type, string code)
    {
        Connection connection = BuildConnection();
        connection.SystemChannelTemplate = new SystemChannelTemplate { Id = connection.SystemChannelTemplateId, Code = code, DisplayName = code, ApiBaseUrl = "https://example.test", ApiVersion = "v3" };
        _connectionRepositoryMock.Setup(r => r.FindByIdWithTemplateAsync(_connectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        SetupDueSchedules(BuildSchedule(type));
        Mock<IWildberriesRateLimiter> limiter = new Mock<IWildberriesRateLimiter>(MockBehavior.Strict);

        await RunSingleTickAsync(BuildGateScheduler(limiter));

        _jobRunRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Once);
        limiter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Tick_ActiveJob_DoesNotConsultLimiter()
    {
        SetupDueSchedules(BuildSchedule(JobType.OrdersImport));
        _jobRunRepositoryMock.Setup(r => r.HasActiveJobAsync(_connectionId, JobType.OrdersImport, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Mock<IWildberriesRateLimiter> limiter = new Mock<IWildberriesRateLimiter>(MockBehavior.Strict);

        await RunSingleTickAsync(BuildGateScheduler(limiter));

        limiter.VerifyNoOtherCalls();
        _jobRunRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private SchedulerHostedService BuildGateScheduler(Mock<IWildberriesRateLimiter> limiter)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(_scheduleRepositoryMock.Object);
        services.AddSingleton(_jobRunRepositoryMock.Object);
        services.AddSingleton(_connectionRepositoryMock.Object);
        services.AddSingleton(_tenantSettingsProviderMock.Object);
        services.AddSingleton(_tenantContext);
        services.AddSingleton(limiter.Object);
        ServiceProvider provider = services.BuildServiceProvider();
        return new SchedulerHostedService(provider.GetRequiredService<IServiceScopeFactory>(), Microsoft.Extensions.Options.Options.Create(new BackgroundJobsOptions()), NullLogger<SchedulerHostedService>.Instance);
    }
}
