using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Finance.Application.Queries;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Tests.Application.Finance.Fakes;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// Tests for the feed freshness query: the answer must carry the server instant, come back stalest
/// first, and keep "arrived but changed nothing" distinguishable from "changed the figures".
/// </summary>
public class GetPnlFeedFreshnessQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_NoCheckpoints_ReturnsEmptyListWithServerInstant()
    {
        // Arrange
        InMemoryPnlFeedStateStore store = new InMemoryPnlFeedStateStore();
        GetPnlFeedFreshnessQueryHandler handler = CreateHandler(store);

        // Act
        PnlFeedFreshnessResponse response = await handler.Handle(new GetPnlFeedFreshnessQuery(), CancellationToken.None);

        // Assert
        response.AsOf.Should().Be(Now);
        response.Channels.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SeveralChannels_OrdersStalestFirst()
    {
        // Arrange
        InMemoryPnlFeedStateStore store = new InMemoryPnlFeedStateStore();
        store.States.Add(CreateState(Now.AddHours(-1)));
        store.States.Add(CreateState(Now.AddDays(-6)));
        store.States.Add(CreateState(Now.AddHours(-3)));

        GetPnlFeedFreshnessQueryHandler handler = CreateHandler(store);

        // Act
        PnlFeedFreshnessResponse response = await handler.Handle(new GetPnlFeedFreshnessQuery(), CancellationToken.None);

        // Assert
        response.Channels.Select(channel => channel.LastBatchAt)
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Handle_ChannelResyncedWithoutChanges_KeepsLastBatchAndStaleChange()
    {
        // Arrange
        DateTimeOffset changedAt = Now.AddDays(-2);
        InMemoryPnlFeedStateStore store = new InMemoryPnlFeedStateStore();

        PnlFeedState state = CreateState(Now);
        state.LastChangeAt = changedAt;
        state.LastBatchAppended = 0;
        state.LastBatchDuplicates = 120;
        store.States.Add(state);

        GetPnlFeedFreshnessQueryHandler handler = CreateHandler(store);

        // Act
        PnlFeedFreshnessResponse response = await handler.Handle(new GetPnlFeedFreshnessQuery(), CancellationToken.None);

        // Assert
        PnlFeedChannelFreshnessView channel = response.Channels.Single();
        channel.LastBatchAt.Should().Be(Now);
        channel.LastChangeAt.Should().Be(changedAt);
        channel.LastBatchAppended.Should().Be(0);
        channel.LastBatchDuplicates.Should().Be(120);
    }

    private static GetPnlFeedFreshnessQueryHandler CreateHandler(InMemoryPnlFeedStateStore store)
    {
        return new GetPnlFeedFreshnessQueryHandler(store, new FixedTimeProvider(Now));
    }

    private static PnlFeedState CreateState(DateTimeOffset lastBatchAt)
    {
        return new PnlFeedState
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            LastJobRunId = Guid.NewGuid(),
            LastBatchAt = lastBatchAt,
            LastChangeAt = lastBatchAt,
            LastBatchAppended = 10,
            LastBatchDuplicates = 0,
            LastBatchFailed = 0,
            CreatedAt = lastBatchAt,
        };
    }
}
