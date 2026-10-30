using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

public class OrdersImportUnitSinkTests
{
    private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<IJobCheckpointRepository> _jobCheckpointRepo = new();

    private OrdersImportUnitSink CreateSink(JobRun jobRun, Connection connection, OrdersImportBatchCursor cursor)
    {
        OrdersImportCheckpointStore checkpointStore = new OrdersImportCheckpointStore(_jobCheckpointRepo.Object, _timeProvider);
        OrdersBatchPublisher publisher = new OrdersBatchPublisher(_eventPublisher.Object, checkpointStore);

        return new OrdersImportUnitSink(publisher, jobRun, connection, cursor);
    }

    private static JobRun BuildJobRun()
    {
        return new JobRun { Id = Guid.NewGuid(), ConnectionId = Guid.NewGuid(), JobType = JobType.OrdersImport };
    }

    private static Connection BuildConnection(JobRun jobRun)
    {
        return new Connection { Id = jobRun.ConnectionId, ChannelId = Guid.NewGuid() };
    }

    private static ChannelOrderSnapshot BuildOrder(string externalOrderId)
    {
        return new ChannelOrderSnapshot
        {
            ExternalOrderId = externalOrderId,
            ExternalOrderNumber = externalOrderId,
            OrderDate = DateTimeOffset.UtcNow.AddHours(-1),
            StatusChangedAt = DateTimeOffset.UtcNow,
            ExternalStatus = "new",
            Currency = "RUB",
            Lines = new List<ChannelOrderLineSnapshot>(),
        };
    }

    [Fact]
    public async Task PublishAsync_PublishesUnitUnderBoundRunAndConnection()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        OrdersImportBatchCursor cursor = new OrdersImportBatchCursor(4, DateTimeOffset.UtcNow, true);
        List<OrdersBatchImportRequestedEvent> published = new List<OrdersBatchImportRequestedEvent>();

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OrdersBatchImportRequestedEvent, CancellationToken>((batch, _) => published.Add(batch))
            .Returns(Task.CompletedTask);

        await CreateSink(jobRun, connection, cursor).PublishAsync(
            new List<ChannelOrderSnapshot> { BuildOrder("ORDER-1") }, CancellationToken.None);

        // The sink is the named form of the closure the local publisher used to carry: the run and
        // connection come from the sink, never from the caller of PublishAsync.
        published.Should().ContainSingle();
        published[0].JobRunId.Should().Be(jobRun.Id);
        published[0].ConnectionId.Should().Be(connection.Id);
        published[0].ChannelId.Should().Be(connection.ChannelId);
        published[0].IsTerminal.Should().BeFalse();
        published[0].BatchId.Should().Be(OrdersBatchPublisher.CreateBatchId(jobRun.Id, 4));
        published[0].Orders.Select(o => o.ExternalOrderId).Should().Equal("ORDER-1");
    }

    [Fact]
    public async Task PublishAsync_AdvancesTheSharedCursor()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        OrdersImportBatchCursor cursor = new OrdersImportBatchCursor(0, DateTimeOffset.UtcNow, false);
        OrdersImportUnitSink sink = CreateSink(jobRun, connection, cursor);

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await sink.PublishAsync(new List<ChannelOrderSnapshot> { BuildOrder("ORDER-1") }, CancellationToken.None);
        await sink.PublishAsync(new List<ChannelOrderSnapshot> { BuildOrder("ORDER-2") }, CancellationToken.None);

        // The cursor is shared with the orchestrator, which reads NextIndex to persist TotalBatches
        // and to number the terminal batch: the sink must advance it, not keep a private copy.
        cursor.NextIndex.Should().Be(2);
    }

    [Fact]
    public async Task PublishAsync_EmptyUnit_PublishesNothingAndDoesNotAdvanceCursor()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        OrdersImportBatchCursor cursor = new OrdersImportBatchCursor(3, DateTimeOffset.UtcNow, true);

        await CreateSink(jobRun, connection, cursor).PublishAsync(
            new List<ChannelOrderSnapshot>(), CancellationToken.None);

        cursor.NextIndex.Should().Be(3);
        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _jobCheckpointRepo.Verify(
            r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
