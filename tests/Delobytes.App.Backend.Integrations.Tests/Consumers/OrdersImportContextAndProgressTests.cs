using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

public class OrdersImportContextAndProgressTests
{
    private static OrdersImportContext BuildContext(JobRun jobRun, Connection connection, DateTimeOffset windowTo, bool isBackfill)
    {
        IOrdersSource ordersSource = new Mock<IOrdersSource>().Object;
        OrdersBackfillState backfillState = new OrdersBackfillState();

        return new OrdersImportContext(
            jobRun,
            connection,
            ordersSource,
            backfillState,
            DateTimeOffset.UtcNow.AddMinutes(10),
            windowTo,
            isBackfill);
    }

    private static OrdersRunProgress BuildProgress(int nextBatchIndex)
    {
        return new OrdersRunProgress(nextBatchIndex);
    }

    [Fact]
    public void Constructor_SetsNextBatchIndexWindowToAndIsBackfill()
    {
        JobRun jobRun = new JobRun { Id = Guid.NewGuid(), ConnectionId = Guid.NewGuid() };
        Connection connection = new Connection { Id = jobRun.ConnectionId, ChannelId = Guid.NewGuid() };
        DateTimeOffset windowTo = DateTimeOffset.UtcNow;

        OrdersImportContext context = BuildContext(jobRun, connection, windowTo, true);
        OrdersRunProgress progress = BuildProgress(5);

        context.IsBackfill.Should().BeTrue();
        context.WindowTo.Should().Be(windowTo);
        progress.NextBatchIndex.Should().Be(5);
    }

    [Fact]
    public void Constructor_StartsTheRunWithZeroCounters()
    {
        JobRun jobRun = new JobRun { Id = Guid.NewGuid(), ConnectionId = Guid.NewGuid() };
        Connection connection = new Connection { Id = jobRun.ConnectionId, ChannelId = Guid.NewGuid() };
        OrdersImportContext context = BuildContext(jobRun, connection, DateTimeOffset.UtcNow, false);
        OrdersRunProgress progress = BuildProgress(0);

        progress.RequestsIssued.Should().Be(0);
        progress.InProcessWaits.Should().Be(0);
        progress.TotalWaited.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void TakeNextBatchIndex_ReturnsCurrentIndexThenIncrements()
    {
        OrdersRunProgress progress = BuildProgress(7);

        int first = progress.TakeNextBatchIndex();
        int second = progress.TakeNextBatchIndex();

        first.Should().Be(7);
        second.Should().Be(8);
        progress.NextBatchIndex.Should().Be(9);
    }

    [Fact]
    public void TakeNextBatchIndex_StartingFromZero_AdvancesSequentially()
    {
        OrdersRunProgress progress = BuildProgress(0);

        List<int> taken = new List<int>
        {
            progress.TakeNextBatchIndex(),
            progress.TakeNextBatchIndex(),
            progress.TakeNextBatchIndex(),
        };

        taken.Should().Equal(0, 1, 2);
        progress.NextBatchIndex.Should().Be(3);
    }
}
