using System;
using System.Collections.Generic;
using System.Linq;
using Delobytes.App.Backend.Finance.Application.Attribution;
using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// Tests for PnlEntryAttribution. The rules are shared by both channels and are the only place a
/// journal entry is bound to a sale, so these tests pin the order of narrowing: unit identity first,
/// product second, order plus product for an accrual without a posting.
/// </summary>
public class PnlEntryAttributionTests
{
    private static readonly Guid ChannelId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Attribute_WbEntryWithSrid_MatchesTheLine()
    {
        // Arrange
        OrderLineRef line = Line("line-1", externalLineId: "srid-1", externalProductId: "174371959");

        PnlEntry entry = Entry("srid-1", null, "174371959");

        // Act
        PnlEntryAttributionResult result = PnlEntryAttribution.Attribute(new[] { entry }, new[] { line });

        // Assert
        result.Matches.Should().ContainSingle();
        result.Matches[0].OrderLineId.Should().Be(line.OrderLineId);
        result.UnmatchedEntries.Should().BeEmpty();
        result.AmbiguousAttributions.Should().Be(0);
    }

    [Fact]
    public void Attribute_OzonMultiSkuPosting_NarrowsByProduct()
    {
        // Arrange: one posting (line identity) holding two products.
        OrderLineRef first = Line("line-1", externalLineId: "posting-1", externalProductId: "sku-1");
        OrderLineRef second = Line("line-2", externalLineId: "posting-1", externalProductId: "sku-2");

        PnlEntry entry = Entry("posting-1", null, "sku-2");

        // Act
        PnlEntryAttributionResult result = PnlEntryAttribution.Attribute(
            new[] { entry },
            new[] { first, second });

        // Assert
        result.Matches.Should().ContainSingle();
        result.Matches[0].OrderLineId.Should().Be(second.OrderLineId);
        result.AmbiguousAttributions.Should().Be(0);
    }

    [Fact]
    public void Attribute_OzonItemEntry_MatchesByOrderAndProduct()
    {
        // Arrange: the accrual carries no posting, only the order number and the SKU.
        OrderLineRef line = Line("line-1", externalLineId: "posting-1", externalProductId: "sku-1");

        PnlEntry entry = Entry(null, "14512295-0639", "sku-1");

        // Act
        PnlEntryAttributionResult result = PnlEntryAttribution.Attribute(new[] { entry }, new[] { line });

        // Assert
        result.Matches.Should().ContainSingle();
        result.Matches[0].OrderLineId.Should().Be(line.OrderLineId);
        result.UnmatchedEntries.Should().BeEmpty();
    }

    [Fact]
    public void Attribute_SeveralItemsForOneOrderAndProduct_ChoosesSmallestExternalLineIdAndCountsIt()
    {
        // Arrange: two units of the same product in one order, and an accrual that names neither.
        OrderLineRef first = Line("line-b", externalLineId: "b-posting", externalProductId: "sku-1");
        OrderLineRef second = Line("line-a", externalLineId: "a-posting", externalProductId: "sku-1");

        PnlEntry entry = Entry(null, "14512295-0639", "sku-1");

        // Act
        PnlEntryAttributionResult result = PnlEntryAttribution.Attribute(
            new[] { entry },
            new[] { first, second });

        // Assert: the choice is deterministic and reported.
        result.Matches.Should().ContainSingle();
        result.Matches[0].OrderLineId.Should().Be(second.OrderLineId);
        result.AmbiguousAttributions.Should().Be(1);
    }

    [Fact]
    public void Attribute_NoMatch_ReportsTheEntryAsUnmatched()
    {
        // Arrange
        OrderLineRef line = Line("line-1", externalLineId: "srid-1", externalProductId: "174371959");

        PnlEntry entry = Entry("srid-unknown", null, "174371959");

        // Act
        PnlEntryAttributionResult result = PnlEntryAttribution.Attribute(new[] { entry }, new[] { line });

        // Assert
        result.Matches.Should().BeEmpty();
        result.UnmatchedEntries.Should().ContainSingle().Which.Should().BeSameAs(entry);
        result.AmbiguousAttributions.Should().Be(0);
    }

    [Fact]
    public void Attribute_CandidateProjection_MatchesLikeTheOrderLineEntity()
    {
        // Arrange: diagnostics loads only the five facts, not whole OrderLineRef entities.
        AttributionCandidate candidate = new AttributionCandidate(
            Guid.NewGuid(),
            ChannelId,
            "14512295-0639",
            "srid-1",
            "174371959");

        PnlEntry entry = Entry("srid-1", null, "174371959");

        // Act
        PnlEntryAttributionResult result = PnlEntryAttribution.Attribute(new[] { entry }, new[] { candidate });

        // Assert
        result.Matches.Should().ContainSingle();
        result.Matches[0].OrderLineId.Should().Be(candidate.OrderLineId);
        result.UnmatchedEntries.Should().BeEmpty();
        result.AmbiguousAttributions.Should().Be(0);
    }

    private static OrderLineRef Line(string id, string? externalLineId, string externalProductId)
    {
        return new OrderLineRef
        {
            OrderLineId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            ChannelId = ChannelId,
            ExternalOrderId = "14512295-0639",
            ExternalLineId = externalLineId,
            ExternalProductId = externalProductId,
            Quantity = 1,
            OrderDate = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero),
            Currency = "RUB",
            UpdatedAt = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero),
        };
    }

    private static PnlEntry Entry(string? externalLineId, string? externalOrderId, string externalProductId)
    {
        return new PnlEntry
        {
            Id = Guid.NewGuid(),
            Source = externalLineId == null ? PnlSource.OzonAccrual : PnlSource.WbReport,
            ReportInstanceKey = externalLineId == null ? string.Empty : "799692300:2026-08-03",
            ExternalEntryId = "3131711029843",
            ChannelId = ChannelId,
            ExternalOrderId = externalOrderId,
            ExternalLineId = externalLineId,
            ExternalProductId = externalProductId,
            Kind = PnlEntryKind.Component,
            Type = PnlEntryType.Sale,
            Amount = 100m,
            Direction = PnlEntryDirection.Income,
            Currency = "RUB",
            DocumentDate = new DateOnly(2026, 8, 1),
            PeriodFrom = new DateOnly(2026, 7, 27),
            PeriodTo = new DateOnly(2026, 8, 2),
        };
    }
}
