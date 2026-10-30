using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Defect D4: a backfill stopped by a throttle or by the run budget used to write
/// <c>IsFetchCompleted = true</c> and publish the terminal batch, finalising a run whose data was
/// incomplete. A stop is now the same outcome as on the delta branch: progress is persisted, the
/// run is not finalised, and a continuation carries the retry.
/// </summary>
public partial class ProcessOrdersImportConsumerBackfillTests
{
    [Fact]
    public async Task ProcessAsync_ThrottledBackfill_PublishesNoTerminalBatchAndKeepsFetchIncomplete()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun jobRun = BuildJobRun(connection);
        OrdersImportCheckpointPayload? persistedPayload = null;

        SetupRun(jobRun);
        CapturePersistedPayload(payload => persistedPayload = payload);
        UseStateStore();

        UseSource((request, call) => Task.FromResult(call == 1
            ? new OrdersPage { Orders = BuildOrdersForDay(request.DateFrom), HasMore = false }
            : new OrdersPage { Orders = new List<ChannelOrderSnapshot>(), Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddHours(3) }));

        await RunAsync(jobRun, connection, CreateConsumer());

        _eventPublisher.Verify(
            p => p.PublishAsync(It.Is<OrdersBatchImportRequestedEvent>(b => b.IsTerminal), It.IsAny<CancellationToken>()),
            Times.Never);

        persistedPayload.Should().NotBeNull();
        persistedPayload!.IsFetchCompleted.Should().BeFalse();
        persistedPayload.IsBackfillCompleted.Should().BeFalse();
        persistedPayload.HandoverWatermark.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_ThrottledBackfill_StillPublishesTheDaysThatWereFetched()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun jobRun = BuildJobRun(connection);

        SetupRun(jobRun);
        UseStateStore();

        UseSource((request, call) => Task.FromResult(call == 1
            ? new OrdersPage { Orders = BuildOrdersForDay(request.DateFrom), HasMore = false }
            : new OrdersPage { Orders = new List<ChannelOrderSnapshot>(), Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddHours(3) }));

        await RunAsync(jobRun, connection, CreateConsumer());

        // The fetched day is real work: dropping it on a stop would re-fetch it on every retry and
        // make no progress at all on a connection that is throttled every run.
        PublishedOrderIds().Should().HaveCount(2);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.Is<OrdersBatchImportRequestedEvent>(b => !b.IsTerminal), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ThrottledBackfill_ReportsProgressCountNotFinalCount()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun jobRun = BuildJobRun(connection);
        OrdersImportCheckpointPayload? persistedPayload = null;

        SetupRun(jobRun);
        CapturePersistedPayload(payload => persistedPayload = payload);
        UseStateStore();

        UseSource((request, call) => Task.FromResult(call == 1
            ? new OrdersPage { Orders = BuildOrdersForDay(request.DateFrom, OrdersImportConstants.BatchSize + 1), HasMore = false }
            : new OrdersPage { Orders = new List<ChannelOrderSnapshot>(), Throttled = true, RetryAt = _timeProvider.GetUtcNow().AddHours(3) }));

        await RunAsync(jobRun, connection, CreateConsumer());

        // Two batches went out (30 and 1 orders) and the count is the progress count, not the
        // final count plus one, because no terminal batch exists to be counted.
        persistedPayload.Should().NotBeNull();
        persistedPayload!.TotalBatches.Should().Be(2);
        persistedPayload.IsFetchCompleted.Should().BeFalse();
        persistedPayload.PagesFetched.Should().Be(2);
    }

    [Fact]
    public async Task ProcessAsync_BudgetExhaustedBackfill_SchedulesContinuationAtBudgetResumeInstant()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun jobRun = BuildJobRun(connection);
        DateTimeOffset startedAt = _timeProvider.GetUtcNow();

        SetupRun(jobRun);
        UseStateStore();

        UseSource((request, call) =>
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(1));

            return Task.FromResult(new OrdersPage { Orders = BuildOrdersForDay(request.DateFrom), HasMore = false });
        });

        await RunAsync(jobRun, connection, CreateConsumer(backfillRunBudgetSeconds: 1));

        // The continuation must not be scheduled in the past: the worker picks up due runs, so a
        // resume instant at or before "now" would spin.
        _jobRunRepo.Verify(
            r => r.CreateAsync(
                It.Is<JobRun>(next =>
                    next.Status == JobStatus.Pending
                    && next.JobType == JobType.OrdersImport
                    && next.ConnectionId == connection.Id
                    && next.NotBefore >= startedAt),
                It.IsAny<CancellationToken>()),
            Times.Once);

        jobRun.Status.Should().NotBe(JobStatus.Failed);
    }

    [Fact]
    public async Task ProcessAsync_CompletedBackfill_PublishesTerminalAndSchedulesNoContinuation()
    {
        Connection connection = BuildConnection(BuildWildberriesTemplate());
        JobRun jobRun = BuildJobRun(connection);
        OrdersImportCheckpointPayload? persistedPayload = null;

        SetupRun(jobRun);
        CapturePersistedPayload(payload => persistedPayload = payload);
        UseStateStore();
        UseInstantCompleteSource();

        await RunAsync(jobRun, connection, CreateConsumer());

        _eventPublisher.Verify(
            p => p.PublishAsync(It.Is<OrdersBatchImportRequestedEvent>(b => b.IsTerminal), It.IsAny<CancellationToken>()),
            Times.Once);
        _jobRunRepo.Verify(r => r.CreateAsync(It.IsAny<JobRun>(), It.IsAny<CancellationToken>()), Times.Never);

        persistedPayload.Should().NotBeNull();
        persistedPayload!.IsFetchCompleted.Should().BeTrue();
        persistedPayload.IsBackfillCompleted.Should().BeTrue();
    }
}
