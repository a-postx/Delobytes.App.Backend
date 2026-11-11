using System;
using System.Collections.Generic;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Orders;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

public class OrdersImportContextAndProgressTests
{
    private static OrdersImportContext BuildContext(JobRun jobRun, Connection connection, DateTimeOffset windowTo, bool isBackfill)
    {
        IOrdersSource ordersSource = new Mock<IOrdersSource>().Object;

        return OrdersImportContextBuilder.Build(
            jobRun: jobRun,
            connection: connection,
            ordersSource: ordersSource,
            windowTo: windowTo,
            isBackfill: isBackfill);
    }

    private static BatchSequence BuildBatches(int nextIndex)
    {
        return new BatchSequence(nextIndex);
    }

    [Fact]
    public void Context_CarriesWindowToAndBranch()
    {
        JobRun jobRun = new JobRun { Id = Guid.NewGuid(), ConnectionId = Guid.NewGuid() };
        Connection connection = new Connection { Id = jobRun.ConnectionId, ChannelId = Guid.NewGuid() };
        DateTimeOffset windowTo = DateTimeOffset.UtcNow;

        OrdersImportContext context = BuildContext(jobRun, connection, windowTo, true);
        BatchSequence batches = BuildBatches(5);

        context.IsBackfill.Should().BeTrue();
        context.WindowTo.Should().Be(windowTo);
        batches.NextIndex.Should().Be(5);
    }

    [Fact]
    public void RunCounters_StartAtZero()
    {
        JobRun jobRun = new JobRun { Id = Guid.NewGuid(), ConnectionId = Guid.NewGuid() };
        Connection connection = new Connection { Id = jobRun.ConnectionId, ChannelId = Guid.NewGuid() };
        BuildContext(jobRun, connection, DateTimeOffset.UtcNow, false);
        RunCounters counters = new RunCounters();

        counters.RequestsIssued.Should().Be(0);
        counters.InProcessWaits.Should().Be(0);
        counters.TotalWaited.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void TakeNext_ReturnsCurrentIndexThenIncrements()
    {
        BatchSequence batches = BuildBatches(7);

        int first = batches.TakeNext();
        int second = batches.TakeNext();

        first.Should().Be(7);
        second.Should().Be(8);
        batches.NextIndex.Should().Be(9);
    }

    [Fact]
    public void TakeNext_StartingFromZero_AdvancesSequentially()
    {
        BatchSequence batches = BuildBatches(0);

        List<int> taken = new List<int>
        {
            batches.TakeNext(),
            batches.TakeNext(),
            batches.TakeNext(),
        };

        taken.Should().Equal(0, 1, 2);
        batches.NextIndex.Should().Be(3);
    }
}
