using System;
using System.Collections.Generic;
using Delobytes.App.Backend.Sales.Domain.Enums;
using Delobytes.App.Backend.Sales.Infrastructure.Messaging.Consumers;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Sales;

/// <summary>
/// Pins <see cref="OrdersBatchImportRequestedConsumer.MapStatus"/> for both wired channels.
/// The projection is the only place where a verbatim channel status becomes a canonical
/// OrderStatus, so a missing Ozon arm silently degrades to Unknown and skews revenue reporting;
/// the Wildberries table is asserted alongside it so widening the vocabulary for one channel
/// cannot quietly re-map another.
/// </summary>
public class OrdersBatchImportStatusMappingTests
{
    /// <summary>
    /// Every Ozon posting status from the channel reference, including the ones reserved for the
    /// FBS feed, which already arrives through the same header-status passthrough.
    /// </summary>
    public static IEnumerable<object[]> OzonStatuses =>
        new List<object[]>
        {
            new object[] { "awaiting_registration", OrderStatus.Created },
            new object[] { "awaiting_approve", OrderStatus.Processing },
            new object[] { "awaiting_packaging", OrderStatus.Processing },
            new object[] { "acceptance_in_progress", OrderStatus.Processing },
            new object[] { "awaiting_deliver", OrderStatus.Processing },
            new object[] { "driver_pickup", OrderStatus.Shipped },
            new object[] { "sent_by_seller", OrderStatus.Shipped },
            new object[] { "delivering", OrderStatus.Shipped },
            new object[] { "delivered", OrderStatus.Delivered },
            new object[] { "cancelled", OrderStatus.Cancelled },
            new object[] { "arbitration", OrderStatus.Unresolved },
            new object[] { "client_arbitration", OrderStatus.Unresolved },
            new object[] { "not_accepted", OrderStatus.Unresolved },
        };

    /// <summary>
    /// The Wildberries vocabulary as it stood before the Ozon arms were added. Regression table:
    /// each of these must keep projecting to the same value.
    /// </summary>
    public static IEnumerable<object[]> WildberriesStatuses =>
        new List<object[]>
        {
            new object[] { "active", OrderStatus.Processing },
            new object[] { "created", OrderStatus.Created },
            new object[] { "new", OrderStatus.Created },
            new object[] { "processing", OrderStatus.Processing },
            new object[] { "confirm", OrderStatus.Processing },
            new object[] { "assembling", OrderStatus.Processing },
            new object[] { "shipped", OrderStatus.Shipped },
            new object[] { "sorted", OrderStatus.Shipped },
            new object[] { "sent", OrderStatus.Shipped },
            new object[] { "delivered", OrderStatus.Delivered },
            new object[] { "sold", OrderStatus.Delivered },
            new object[] { "buyout", OrderStatus.Delivered },
            new object[] { "cancelled", OrderStatus.Cancelled },
            new object[] { "canceled", OrderStatus.Cancelled },
            new object[] { "cancel", OrderStatus.Cancelled },
            new object[] { "returned", OrderStatus.Returned },
            new object[] { "return", OrderStatus.Returned },
            new object[] { "partiallyreturned", OrderStatus.PartiallyReturned },
            new object[] { "partially_returned", OrderStatus.PartiallyReturned },
            new object[] { "unresolved", OrderStatus.Unresolved },
            new object[] { "dispute", OrderStatus.Unresolved },
        };

    /// <summary>
    /// Verifies that every Ozon status projects to the documented OrderStatus.
    /// </summary>
    /// <param name="externalStatus">Verbatim Ozon status.</param>
    /// <param name="expected">Expected projection.</param>
    [Theory]
    [MemberData(nameof(OzonStatuses))]
    public void MapStatus_OzonStatus_ProjectsToExpectedOrderStatus(string externalStatus, OrderStatus expected)
    {
        OrderStatus actual = OrdersBatchImportRequestedConsumer.MapStatus(externalStatus);

        actual.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that the third-party vocabulary stays stable for the Wildberries feed.
    /// </summary>
    /// <param name="externalStatus">Verbatim Wildberries status.</param>
    /// <param name="expected">Expected projection.</param>
    [Theory]
    [MemberData(nameof(WildberriesStatuses))]
    public void MapStatus_WildberriesSynonym_StillProjectsToExpectedOrderStatus(string externalStatus, OrderStatus expected)
    {
        OrderStatus actual = OrdersBatchImportRequestedConsumer.MapStatus(externalStatus);

        actual.Should().Be(expected);
    }

    /// <summary>
    /// An Ozon status the projection does not know must degrade to Unknown instead of throwing:
    /// Ozon adds statuses over time and a batch must not fail over one unknown posting.
    /// </summary>
    /// <param name="externalStatus">An unrecognised Ozon status.</param>
    [Theory]
    [InlineData("awaiting_return_from_client")]
    [InlineData("returned_to_seller")]
    [InlineData("some_future_ozon_status")]
    public void MapStatus_UnknownOzonStatus_ProjectsToUnknown(string externalStatus)
    {
        OrderStatus actual = OrdersBatchImportRequestedConsumer.MapStatus(externalStatus);

        actual.Should().Be(OrderStatus.Unknown);
    }

    /// <summary>
    /// Ozon statuses arrive verbatim from the posting payload, so the match must be trimmed and
    /// case-insensitive rather than relying on the channel to normalise them.
    /// </summary>
    [Theory]
    [InlineData("AWAITING_PACKAGING")]
    [InlineData("Awaiting_Packaging")]
    [InlineData("  awaiting_packaging  ")]
    [InlineData("\tawaiting_packaging\n")]
    public void MapStatus_OzonStatusWithNoise_StillProjectsCorrectly(string externalStatus)
    {
        OrderStatus actual = OrdersBatchImportRequestedConsumer.MapStatus(externalStatus);

        actual.Should().Be(OrderStatus.Processing);
    }

    /// <summary>
    /// An order without a status is a legitimate state (the header reduction reports none when
    /// every line is cancelled); it must not throw or acquire a meaning.
    /// </summary>
    /// <param name="externalStatus">Missing or blank status.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MapStatus_MissingStatus_ProjectsToUnknown(string? externalStatus)
    {
        OrderStatus actual = OrdersBatchImportRequestedConsumer.MapStatus(externalStatus!);

        actual.Should().Be(OrderStatus.Unknown);
    }
}
