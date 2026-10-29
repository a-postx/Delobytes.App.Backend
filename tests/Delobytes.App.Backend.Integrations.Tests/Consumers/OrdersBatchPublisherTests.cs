using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

public class OrdersBatchPublisherTests
{
    private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly Mock<IJobCheckpointRepository> _jobCheckpointRepo = new();

    private OrdersBatchPublisher CreatePublisher()
    {
        OrdersImportCheckpointStore checkpointStore = new OrdersImportCheckpointStore(_jobCheckpointRepo.Object, _timeProvider);
        return new OrdersBatchPublisher(_eventPublisher.Object, checkpointStore);
    }

    private static JobRun BuildJobRun()
    {
        return new JobRun { Id = Guid.NewGuid(), ConnectionId = Guid.NewGuid() };
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
            OrderDate = DateTimeOffset.UtcNow,
            ExternalStatus = "new",
            Currency = "RUB",
        };
    }

    [Fact]
    public async Task PublishUnitAsync_EmptyUnit_PublishesNothingAndWritesNoCheckpoint()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        OrdersImportBatchCursor cursor = new OrdersImportBatchCursor(0, DateTimeOffset.UtcNow, false);
        OrdersBatchPublisher publisher = CreatePublisher();

        await publisher.PublishUnitAsync(jobRun, connection, cursor, new List<ChannelOrderSnapshot>(), CancellationToken.None);

        _eventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _jobCheckpointRepo.Verify(
            r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()),
            Times.Never);
        cursor.NextIndex.Should().Be(0);
    }

    [Fact]
    public async Task PublishUnitAsync_OneOrder_PublishesOneBatchAndAdvancesCursor()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        DateTimeOffset windowTo = DateTimeOffset.UtcNow;
        OrdersImportBatchCursor cursor = new OrdersImportBatchCursor(3, windowTo, true);
        OrdersBatchPublisher publisher = CreatePublisher();
        List<OrdersBatchImportRequestedEvent> published = new List<OrdersBatchImportRequestedEvent>();

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OrdersBatchImportRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        List<ChannelOrderSnapshot> unit = new List<ChannelOrderSnapshot> { BuildOrder("ORDER-1") };

        await publisher.PublishUnitAsync(jobRun, connection, cursor, unit, CancellationToken.None);

        published.Should().ContainSingle();
        published[0].JobRunId.Should().Be(jobRun.Id);
        published[0].ConnectionId.Should().Be(connection.Id);
        published[0].ChannelId.Should().Be(connection.ChannelId);
        published[0].BatchId.Should().Be(OrdersBatchPublisher.CreateBatchId(jobRun.Id, 3));
        published[0].IsTerminal.Should().BeFalse();
        cursor.NextIndex.Should().Be(4);
    }

    [Fact]
    public async Task PublishUnitAsync_SavesProgressCheckpointWithCursorFacts()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        DateTimeOffset windowTo = DateTimeOffset.UtcNow;
        OrdersImportBatchCursor cursor = new OrdersImportBatchCursor(0, windowTo, true);
        OrdersBatchPublisher publisher = CreatePublisher();
        JobCheckpoint? savedCheckpoint = null;

        _jobCheckpointRepo
            .Setup(r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<JobCheckpoint, CancellationToken>((checkpoint, _) => savedCheckpoint = checkpoint)
            .Returns(Task.CompletedTask);

        List<ChannelOrderSnapshot> unit = new List<ChannelOrderSnapshot> { BuildOrder("ORDER-1") };

        await publisher.PublishUnitAsync(jobRun, connection, cursor, unit, CancellationToken.None);

        savedCheckpoint.Should().NotBeNull();
        savedCheckpoint!.Payload.Should().Contain("\"TotalBatches\":1");
        savedCheckpoint.Payload.Should().Contain("\"IsFetchCompleted\":false");
        savedCheckpoint.Payload.Should().Contain("\"IsBackfill\":true");
    }

    [Fact]
    public async Task PublishUnitAsync_MoreThanOneBatchWorthOfOrders_SplitsIntoMultipleBatches()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        OrdersImportBatchCursor cursor = new OrdersImportBatchCursor(0, DateTimeOffset.UtcNow, false);
        OrdersBatchPublisher publisher = CreatePublisher();
        List<OrdersBatchImportRequestedEvent> published = new List<OrdersBatchImportRequestedEvent>();

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OrdersBatchImportRequestedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        List<ChannelOrderSnapshot> unit = Enumerable
            .Range(0, ProcessOrdersImportConsumer.BatchSize + 1)
            .Select(i => BuildOrder($"ORDER-{i}"))
            .ToList();

        await publisher.PublishUnitAsync(jobRun, connection, cursor, unit, CancellationToken.None);

        published.Should().HaveCount(2);
        published[0].Orders.Should().HaveCount(ProcessOrdersImportConsumer.BatchSize);
        published[1].Orders.Should().HaveCount(1);
        published.Select(p => p.BatchId).Should().Equal(
            OrdersBatchPublisher.CreateBatchId(jobRun.Id, 0),
            OrdersBatchPublisher.CreateBatchId(jobRun.Id, 1));
        cursor.NextIndex.Should().Be(2);
    }

    [Fact]
    public async Task PublishTerminalAsync_PublishesEmptyTerminalBatchAndWritesNoCheckpoint()
    {
        JobRun jobRun = BuildJobRun();
        Connection connection = BuildConnection(jobRun);
        OrdersBatchPublisher publisher = CreatePublisher();
        OrdersBatchImportRequestedEvent? published = null;

        _eventPublisher
            .Setup(p => p.PublishAsync(It.IsAny<OrdersBatchImportRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OrdersBatchImportRequestedEvent, CancellationToken>((e, _) => published = e)
            .Returns(Task.CompletedTask);

        await publisher.PublishTerminalAsync(jobRun, connection, 5, CancellationToken.None);

        published.Should().NotBeNull();
        published!.IsTerminal.Should().BeTrue();
        published.Orders.Should().BeEmpty();
        published.BatchId.Should().Be(OrdersBatchPublisher.CreateBatchId(jobRun.Id, 5));
        _jobCheckpointRepo.Verify(
            r => r.UpsertAsync(It.IsAny<JobCheckpoint>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void CreateBatchId_IsDeterministic()
    {
        Guid jobRunId = Guid.NewGuid();

        Guid first = OrdersBatchPublisher.CreateBatchId(jobRunId, 0);
        Guid second = OrdersBatchPublisher.CreateBatchId(jobRunId, 0);

        first.Should().Be(second);
    }

    [Fact]
    public void CreateBatchId_DiffersByIndex()
    {
        Guid jobRunId = Guid.NewGuid();

        Guid batch0 = OrdersBatchPublisher.CreateBatchId(jobRunId, 0);
        Guid batch1 = OrdersBatchPublisher.CreateBatchId(jobRunId, 1);

        batch0.Should().NotBe(batch1);
    }

    [Fact]
    public void SplitIntoBatches_EmptyOrders_ReturnsOneEmptyBatch()
    {
        List<List<ChannelOrderSnapshot>> batches = OrdersBatchPublisher.SplitIntoBatches(new List<ChannelOrderSnapshot>());

        batches.Should().ContainSingle();
        batches[0].Should().BeEmpty();
    }

    [Fact]
    public void SplitIntoBatches_ExactlyBatchSize_ReturnsOneBatch()
    {
        List<ChannelOrderSnapshot> orders = Enumerable
            .Range(0, ProcessOrdersImportConsumer.BatchSize)
            .Select(i => BuildOrder($"ORDER-{i}"))
            .ToList();

        List<List<ChannelOrderSnapshot>> batches = OrdersBatchPublisher.SplitIntoBatches(orders);

        batches.Should().ContainSingle();
        batches[0].Should().HaveCount(ProcessOrdersImportConsumer.BatchSize);
    }
}
