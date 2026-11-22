using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence;
using Delobytes.App.Backend.Finance.Infrastructure.Persistence.Stores;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Finance;

/// <summary>
/// Guards the checkpoint write: one row per channel, monotonic in time, and a duplicates-only batch
/// keeps the instant the figures last moved.
/// </summary>
public class PnlFeedStateStoreTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ChannelId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordBatchAsync_FirstBatch_CreatesCheckpointWithChangeInstant()
    {
        // Arrange
        using FinanceDbContext context = CreateContext();
        PnlFeedStateStore store = new PnlFeedStateStore(context);

        // Act
        await store.RecordBatchAsync(CreateCheckpoint(Now, appended: 5, duplicates: 0), CancellationToken.None);

        // Assert
        PnlFeedState state = (await store.LoadAllAsync(CancellationToken.None)).Single();
        state.ChannelId.Should().Be(ChannelId);
        state.LastBatchAt.Should().Be(Now);
        state.LastChangeAt.Should().Be(Now);
        state.LastBatchAppended.Should().Be(5);
        state.CreatedAt.Should().Be(Now);
        state.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task RecordBatchAsync_DuplicateOnlyBatch_MovesBatchInstantButNotChangeInstant()
    {
        // Arrange
        using FinanceDbContext context = CreateContext();
        PnlFeedStateStore store = new PnlFeedStateStore(context);

        await store.RecordBatchAsync(CreateCheckpoint(Now, appended: 5, duplicates: 0), CancellationToken.None);

        // Act
        await store.RecordBatchAsync(CreateCheckpoint(Now.AddHours(1), appended: 0, duplicates: 120), CancellationToken.None);

        // Assert
        PnlFeedState state = (await store.LoadAllAsync(CancellationToken.None)).Single();
        state.LastBatchAt.Should().Be(Now.AddHours(1));
        state.LastChangeAt.Should().Be(Now);
        state.LastBatchAppended.Should().Be(0);
        state.LastBatchDuplicates.Should().Be(120);
        state.UpdatedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public async Task RecordBatchAsync_RedeliveredOlderBatch_LeavesCheckpointUntouched()
    {
        // Arrange
        using FinanceDbContext context = CreateContext();
        PnlFeedStateStore store = new PnlFeedStateStore(context);

        await store.RecordBatchAsync(CreateCheckpoint(Now, appended: 5, duplicates: 0), CancellationToken.None);

        // Act
        await store.RecordBatchAsync(CreateCheckpoint(Now.AddHours(-1), appended: 5, duplicates: 0), CancellationToken.None);

        // Assert
        PnlFeedState state = (await store.LoadAllAsync(CancellationToken.None)).Single();
        state.LastBatchAt.Should().Be(Now);
        state.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task RecordBatchAsync_SecondChannel_AddsSecondRow()
    {
        // Arrange
        using FinanceDbContext context = CreateContext();
        PnlFeedStateStore store = new PnlFeedStateStore(context);

        await store.RecordBatchAsync(CreateCheckpoint(Now, appended: 5, duplicates: 0), CancellationToken.None);

        PnlFeedCheckpoint otherChannel = CreateCheckpoint(Now, appended: 1, duplicates: 0) with
        {
            ChannelId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        };

        // Act
        await store.RecordBatchAsync(otherChannel, CancellationToken.None);

        // Assert
        IReadOnlyList<PnlFeedState> states = await store.LoadAllAsync(CancellationToken.None);
        states.Should().HaveCount(2);
    }

    private static PnlFeedCheckpoint CreateCheckpoint(DateTimeOffset completedAt, int appended, int duplicates)
    {
        return new PnlFeedCheckpoint(
            ChannelId,
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            completedAt,
            appended,
            duplicates,
            0);
    }

    private static FinanceDbContext CreateContext()
    {
        DbContextOptions<FinanceDbContext> options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"finance-feed-state-{Guid.NewGuid()}")
            .Options;

        return new FinanceDbContext(options, CreateTenantContext());
    }

    private static ITenantContext CreateTenantContext()
    {
        Mock<ITenantContext> tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(TenantId);

        return tenant.Object;
    }
}
