using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Npgsql;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Verifies that a connection created while the application is running gets its orders schedules
/// without a restart: which job types are scheduled per channel, that the first run is due
/// immediately, and that the consumer neither writes without a tenant nor fails on redelivery.
/// </summary>
public class ConnectionCreatedEventConsumerTests
{
    private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(
        new DateTimeOffset(2026, 11, 1, 9, 30, 0, TimeSpan.Zero));

    private readonly Mock<IConnectionRepository> _connectionRepo = new();
    private readonly Mock<IJobScheduleRepository> _scheduleRepo = new();
    private readonly StubTenantContext _tenantContext = new();

    private readonly Guid _tenantId = Guid.NewGuid();

    public ConnectionCreatedEventConsumerTests()
    {
        _tenantContext.TenantId = _tenantId;
    }

    private ConnectionCreatedEventConsumer CreateConsumer(string cronExpression = "0 * * * *")
    {
        return new ConnectionCreatedEventConsumer(
            _connectionRepo.Object,
            _scheduleRepo.Object,
            _tenantContext,
            Options.Create(new BackgroundJobsOptions { DefaultOrdersImportCronExpression = cronExpression }),
            NullLogger<ConnectionCreatedEventConsumer>.Instance,
            _timeProvider);
    }

    private static Connection BuildConnection(string channelCode, bool isActive = true)
    {
        SystemChannelTemplate template = new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = channelCode,
            DisplayName = channelCode,
            ApiBaseUrl = "https://api.example.test",
            ApiVersion = "v1",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        return new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            SystemChannelTemplateId = template.Id,
            SystemChannelTemplate = template,
            Name = "Test connection",
            ApiKey = "test-api-key",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private void SetupConnection(Connection? connection)
    {
        _connectionRepo
            .Setup(r => r.FindByIdWithTemplateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
    }

    /// <summary>
    /// Installs an AddMissingAsync stub that records the schedules the consumer proposed and
    /// reports every one of them as created.
    /// </summary>
    private List<JobSchedule> CaptureProposedSchedules()
    {
        List<JobSchedule> proposed = new List<JobSchedule>();

        _scheduleRepo
            .Setup(r => r.AddMissingAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<JobSchedule>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyList<JobSchedule>, CancellationToken>(
                (connectionId, schedules, cancellationToken) => proposed.AddRange(schedules))
            .ReturnsAsync((Guid connectionId, IReadOnlyList<JobSchedule> schedules, CancellationToken cancellationToken) =>
                schedules.Count);

        return proposed;
    }

    private void SetupAddMissingResult(int createdCount)
    {
        _scheduleRepo
            .Setup(r => r.AddMissingAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<JobSchedule>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdCount);
    }

    private void SetupAddMissingFailure(Exception exception)
    {
        _scheduleRepo
            .Setup(r => r.AddMissingAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<JobSchedule>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }

    private static ConnectionCreatedEvent BuildEvent(Connection connection)
    {
        return new ConnectionCreatedEvent
        {
            ConnectionId = connection.Id,
            ChannelId = connection.ChannelId,
            SystemChannelTemplateId = connection.SystemChannelTemplateId,
        };
    }

    [Fact]
    public async Task ProcessAsync_OzonConnection_CreatesFboAndFbsSchedulesDueImmediately()
    {
        Connection connection = BuildConnection("ozon");
        SetupConnection(connection);
        List<JobSchedule> proposed = CaptureProposedSchedules();

        await CreateConsumer().ProcessAsync(BuildEvent(connection), CancellationToken.None);

        proposed.Select(s => s.JobType).Should().BeEquivalentTo(
            new[] { JobType.OrdersImportFbo, JobType.OrdersImportFbs },
            "both schemas share one connection and are always scheduled; sequencing is enforced by the scheduler");
        proposed.Should().OnlyContain(s => s.ConnectionId == connection.Id);
        proposed.Should().OnlyContain(s => s.CronExpression == "0 * * * *");
        proposed.Should().OnlyContain(s => s.IsEnabled);
        proposed.Should().OnlyContain(s => s.LastRunAt == null);
        proposed.Should().OnlyContain(
            s => s.NextRunAt == _timeProvider.GetUtcNow(),
            "the first import must start on the next scheduler tick, not at the top of the next hour");
    }

    [Fact]
    public async Task ProcessAsync_WildberriesConnection_CreatesOrdersImportSchedule()
    {
        Connection connection = BuildConnection("wildberries");
        SetupConnection(connection);
        List<JobSchedule> proposed = CaptureProposedSchedules();

        await CreateConsumer().ProcessAsync(BuildEvent(connection), CancellationToken.None);

        proposed.Should().ContainSingle();
        proposed[0].JobType.Should().Be(JobType.OrdersImport);
    }

    [Fact]
    public async Task ProcessAsync_ChannelWithoutOrdersImport_CreatesNoSchedules()
    {
        Connection connection = BuildConnection("yandex.kit");
        SetupConnection(connection);

        await CreateConsumer().ProcessAsync(BuildEvent(connection), CancellationToken.None);

        _scheduleRepo.Verify(
            r => r.AddMissingAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<JobSchedule>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_SchedulesCarryNoTenantId_SoTheContextStampsThem()
    {
        Connection connection = BuildConnection("ozon");
        SetupConnection(connection);
        List<JobSchedule> proposed = CaptureProposedSchedules();

        await CreateConsumer().ProcessAsync(BuildEvent(connection), CancellationToken.None);

        proposed.Should().OnlyContain(
            s => s.TenantId == Guid.Empty,
            "SetTenantId() stamps every Added ITenantScoped row from the ambient context, so the consumer must not duplicate it");
    }

    [Fact]
    public async Task ProcessAsync_EveryProposedScheduleHasDistinctJobType()
    {
        Connection connection = BuildConnection("ozon");
        SetupConnection(connection);
        List<JobSchedule> proposed = CaptureProposedSchedules();

        await CreateConsumer().ProcessAsync(BuildEvent(connection), CancellationToken.None);

        proposed.Select(s => s.JobType).Should().OnlyHaveUniqueItems(
            "the unique (ConnectionId, JobType) index would reject a duplicate inside a single save");
        proposed.Select(s => s.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ProcessAsync_MessageWithoutTenant_DoNotAddAnything()
    {
        _tenantContext.TenantId = null;
        Connection connection = BuildConnection("ozon");

        await CreateConsumer().ProcessAsync(BuildEvent(connection), CancellationToken.None);

        _connectionRepo.Verify(
            r => r.FindByIdWithTemplateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _scheduleRepo.Verify(
            r => r.AddMissingAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<JobSchedule>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ConnectionNotFound_CreatesNoSchedules()
    {
        SetupConnection(null);

        await CreateConsumer().ProcessAsync(
            new ConnectionCreatedEvent { ConnectionId = Guid.NewGuid(), ChannelId = Guid.NewGuid() },
            CancellationToken.None);

        _scheduleRepo.Verify(
            r => r.AddMissingAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<JobSchedule>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_InactiveConnection_CreatesNoSchedules()
    {
        Connection connection = BuildConnection("ozon", isActive: false);
        SetupConnection(connection);

        await CreateConsumer().ProcessAsync(BuildEvent(connection), CancellationToken.None);

        _scheduleRepo.Verify(
            r => r.AddMissingAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<JobSchedule>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_RedeliveryWhenSchedulesAlreadyExist_CompletesWithoutError()
    {
        Connection connection = BuildConnection("ozon");
        SetupConnection(connection);
        SetupAddMissingResult(0);

        Func<Task> act = () => CreateConsumer().ProcessAsync(BuildEvent(connection), CancellationToken.None);

        await act.Should().NotThrowAsync(
            "a repeated delivery must be a no-op, not a failure that MassTransit keeps retrying");
    }

    [Fact]
    public async Task ProcessAsync_UniqueViolationFromConcurrentInsert_IsAbsorbed()
    {
        Connection connection = BuildConnection("ozon");
        SetupConnection(connection);
        SetupAddMissingFailure(new DbUpdateException(
            "duplicate key value violates unique constraint",
            new PostgresException(
                "duplicate key value violates unique constraint \"IX_JobSchedules_ConnectionId_JobType\"",
                "ERROR",
                "ERROR",
                "23505")));

        Func<Task> act = () => CreateConsumer().ProcessAsync(BuildEvent(connection), CancellationToken.None);

        await act.Should().NotThrowAsync(
            "the startup seeder can insert the same rows between the existence check and the save");
    }

    [Fact]
    public async Task ProcessAsync_OtherDatabaseFailure_Propagates()
    {
        Connection connection = BuildConnection("ozon");
        SetupConnection(connection);
        SetupAddMissingFailure(new DbUpdateException(
            "violates foreign key constraint",
            new PostgresException(
                "insert or update on table \"JobSchedules\" violates foreign key constraint",
                "ERROR",
                "ERROR",
                "23503")));

        Func<Task> act = () => CreateConsumer().ProcessAsync(BuildEvent(connection), CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>(
            "only a duplicate is benign; every other failure has to reach MassTransit so the message is retried");
    }

    [Fact]
    public async Task ProcessAsync_CustomCronExpression_IsUsedForTheSchedule()
    {
        Connection connection = BuildConnection("ozon");
        SetupConnection(connection);
        List<JobSchedule> proposed = CaptureProposedSchedules();

        await CreateConsumer(cronExpression: "15 * * * *")
            .ProcessAsync(BuildEvent(connection), CancellationToken.None);

        proposed.Should().OnlyContain(s => s.CronExpression == "15 * * * *");
    }
}

internal sealed class StubTenantContext : ITenantContext
{
    public Guid? TenantId { get; set; }
}
