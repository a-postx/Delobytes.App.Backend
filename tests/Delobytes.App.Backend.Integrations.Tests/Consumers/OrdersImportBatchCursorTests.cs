using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

public class OrdersImportBatchCursorTests
{
    [Fact]
    public void Constructor_SetsNextIndexWindowToAndIsBackfill()
    {
        DateTimeOffset windowTo = DateTimeOffset.UtcNow;

        OrdersImportBatchCursor cursor = new OrdersImportBatchCursor(5, windowTo, true);

        cursor.NextIndex.Should().Be(5);
        cursor.WindowTo.Should().Be(windowTo);
        cursor.IsBackfill.Should().BeTrue();
    }

    [Fact]
    public void TakeNext_ReturnsCurrentIndexThenIncrements()
    {
        OrdersImportBatchCursor cursor = new OrdersImportBatchCursor(7, DateTimeOffset.UtcNow, false);

        int first = cursor.TakeNext();
        int second = cursor.TakeNext();

        first.Should().Be(7);
        second.Should().Be(8);
        cursor.NextIndex.Should().Be(9);
    }

    [Fact]
    public void TakeNext_StartingFromZero_AdvancesSequentially()
    {
        OrdersImportBatchCursor cursor = new OrdersImportBatchCursor(0, DateTimeOffset.UtcNow, false);

        List<int> taken = new List<int> { cursor.TakeNext(), cursor.TakeNext(), cursor.TakeNext() };

        taken.Should().Equal(0, 1, 2);
        cursor.NextIndex.Should().Be(3);
    }
}
