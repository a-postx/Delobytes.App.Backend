using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Application.Orders;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Tests for <see cref="OrdersBackfillWalker.GetOrCreateAsync"/>: the backfill cursor is keyed by
/// (ConnectionId, JobType), so several orders job types on one connection advance independently.
/// </summary>
public class OrdersBackfillWalkerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(Now);
    private readonly Mock<IOrdersBackfillStateRepository> _backfillStateRepo = new();
    private readonly Dictionary<(Guid ConnectionId, JobType JobType), OrdersBackfillState> _store = new();

    private OrdersBackfillWalker CreateWalker()
    {
        OrdersImportCheckpointStore checkpointStore = new OrdersImportCheckpointStore(
            Mock.Of<IJobCheckpointRepository>(),
            _timeProvider);

        OrdersBatchPublisher batchPublisher = new OrdersBatchPublisher(
            Mock.Of<IEventPublisher>(),
            checkpointStore);

        _backfillStateRepo
            .Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<JobType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid connectionId, JobType jobType, CancellationToken _) =>
                _store.TryGetValue((connectionId, jobType), out OrdersBackfillState? state) ? state : null);

        _backfillStateRepo
            .Setup(r => r.UpsertAsync(It.IsAny<OrdersBackfillState>(), It.IsAny<CancellationToken>()))
            .Returns((OrdersBackfillState state, CancellationToken _) =>
            {
                _store[(state.ConnectionId, state.JobType)] = state;
                return Task.CompletedTask;
            });

        return new OrdersBackfillWalker(
            _backfillStateRepo.Object,
            batchPublisher,
            Mock.Of<ILogger<OrdersBackfillWalker>>(),
            _timeProvider);
    }

    [Fact]
    public async Task GetOrCreateAsync_TwoJobTypesOnOneConnection_CreateIndependentCursors()
    {
        Guid connectionId = Guid.NewGuid();
        OrdersBackfillWalker walker = CreateWalker();

        OrdersBackfillState wbState = await walker.GetOrCreateAsync(
            connectionId, JobType.OrdersImport, TestTunings.Wildberries(), CancellationToken.None);
        OrdersBackfillState ozonState = await walker.GetOrCreateAsync(
            connectionId, JobType.OrdersImportFbo, TestTunings.Wildberries(), CancellationToken.None);

        wbState.Id.Should().NotBe(ozonState.Id);
        wbState.JobType.Should().Be(JobType.OrdersImport);
        ozonState.JobType.Should().Be(JobType.OrdersImportFbo);
        wbState.ConnectionId.Should().Be(connectionId);
        ozonState.ConnectionId.Should().Be(connectionId);
    }

    [Fact]
    public async Task GetOrCreateAsync_AdvancingOneJobTypeCursor_LeavesTheOtherUntouched()
    {
        Guid connectionId = Guid.NewGuid();
        OrdersBackfillWalker walker = CreateWalker();

        OrdersBackfillState wbState = await walker.GetOrCreateAsync(
            connectionId, JobType.OrdersImport, TestTunings.Wildberries(), CancellationToken.None);
        OrdersBackfillState ozonState = await walker.GetOrCreateAsync(
            connectionId, JobType.OrdersImportFbo, TestTunings.Wildberries(), CancellationToken.None);

        DateTimeOffset ozonOriginalNextDay = ozonState.NextDayToFetch;

        wbState.NextDayToFetch = wbState.NextDayToFetch.AddDays(-1);
        await _backfillStateRepo.Object.UpsertAsync(wbState, CancellationToken.None);

        OrdersBackfillState reloadedOzonState = await walker.GetOrCreateAsync(
            connectionId, JobType.OrdersImportFbo, TestTunings.Wildberries(), CancellationToken.None);

        reloadedOzonState.NextDayToFetch.Should().Be(ozonOriginalNextDay);
    }

    [Fact]
    public async Task GetOrCreateAsync_ExistingCursor_IsReturnedWithoutCreatingANewOne()
    {
        Guid connectionId = Guid.NewGuid();
        OrdersBackfillWalker walker = CreateWalker();

        OrdersBackfillState created = await walker.GetOrCreateAsync(
            connectionId, JobType.OrdersImport, TestTunings.Wildberries(), CancellationToken.None);
        OrdersBackfillState fetchedAgain = await walker.GetOrCreateAsync(
            connectionId, JobType.OrdersImport, TestTunings.Wildberries(), CancellationToken.None);

        fetchedAgain.Id.Should().Be(created.Id);
        _backfillStateRepo.Verify(
            r => r.UpsertAsync(It.IsAny<OrdersBackfillState>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
