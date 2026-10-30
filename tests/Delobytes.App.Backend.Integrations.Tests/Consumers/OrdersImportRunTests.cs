using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

public class OrdersImportRunTests
{
    private static OrdersImportRun BuildRun(int nextBatchIndex, DateTimeOffset windowTo, bool isBackfill)
    {
        JobRun jobRun = new JobRun { Id = Guid.NewGuid(), ConnectionId = Guid.NewGuid() };
        Connection connection = new Connection { Id = jobRun.ConnectionId, ChannelId = Guid.NewGuid() };

        return new OrdersImportRun(
            jobRun,
            connection,
            DateTimeOffset.UtcNow.AddMinutes(10),
            windowTo,
            isBackfill,
            nextBatchIndex);
    }

    [Fact]
    public void Constructor_SetsNextBatchIndexWindowToAndIsBackfill()
    {
        DateTimeOffset windowTo = DateTimeOffset.UtcNow;

        OrdersImportRun run = BuildRun(5, windowTo, true);

        run.NextBatchIndex.Should().Be(5);
        run.WindowTo.Should().Be(windowTo);
        run.IsBackfill.Should().BeTrue();
    }

    [Fact]
    public void Constructor_StartsTheRunAsCompletedWithZeroCounters()
    {
        OrdersImportRun run = BuildRun(0, DateTimeOffset.UtcNow, false);

        run.StopReason.Should().Be(StopReason.Completed);
        run.ResumeAt.Should().BeNull();
        run.RequestsIssued.Should().Be(0);
        run.RowsWithoutIdentity.Should().Be(0);
        run.InProcessWaits.Should().Be(0);
        run.TotalWaited.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void TakeNextBatchIndex_ReturnsCurrentIndexThenIncrements()
    {
        OrdersImportRun run = BuildRun(7, DateTimeOffset.UtcNow, false);

        int first = run.TakeNextBatchIndex();
        int second = run.TakeNextBatchIndex();

        first.Should().Be(7);
        second.Should().Be(8);
        run.NextBatchIndex.Should().Be(9);
    }

    [Fact]
    public void TakeNextBatchIndex_StartingFromZero_AdvancesSequentially()
    {
        OrdersImportRun run = BuildRun(0, DateTimeOffset.UtcNow, false);

        List<int> taken = new List<int>
        {
            run.TakeNextBatchIndex(),
            run.TakeNextBatchIndex(),
            run.TakeNextBatchIndex(),
        };

        taken.Should().Equal(0, 1, 2);
        run.NextBatchIndex.Should().Be(3);
    }
}
