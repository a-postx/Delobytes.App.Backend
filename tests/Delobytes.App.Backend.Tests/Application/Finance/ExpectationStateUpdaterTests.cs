using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Finance.Application.Expectation;
using Delobytes.App.Backend.Finance.Application.Projection;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Delobytes.App.Backend.Tests.Application.Finance.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// Tests for the aging pass. The materiality threshold is the line between a decision the system may
/// take on its own and one that belongs to an administrator, so both sides of the boundary are pinned.
/// </summary>
public class ExpectationStateUpdaterTests
{
    private static readonly Guid ChannelId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(10.00, PnlExpectationState.ClosedWithException)]
    [InlineData(10.01, PnlExpectationState.Overdue)]
    public async Task RunAsync_AppliesTheMaterialityThreshold(
        decimal estimate,
        PnlExpectationState expectedState)
    {
        // Arrange
        Harness harness = Build(estimate);

        // Act
        int examined = await harness.Updater.RunAsync(CancellationToken.None);

        // Assert
        examined.Should().Be(1);

        PnlLineExpectation expectation = harness.Expectations.Expectations.Should().ContainSingle().Subject;
        expectation.State.Should().Be(expectedState);
        expectation.AmountEstimate.Should().Be(estimate);

        PnlLine line = harness.Lines.Lines[harness.OrderLineId];

        line.CompletenessState.Should().Be(
            expectedState == PnlExpectationState.ClosedWithException
                ? PnlLineCompleteness.ClosedWithException
                : PnlLineCompleteness.Overdue);
    }

    [Fact]
    public async Task RunAsync_NoEstimate_SendsTheExpectationToTheAdministrator()
    {
        // Arrange: nothing comparable to estimate from, so the system must not close it itself.
        Harness harness = Build(estimate: null);

        // Act
        await harness.Updater.RunAsync(CancellationToken.None);

        // Assert
        PnlLineExpectation expectation = harness.Expectations.Expectations.Should().ContainSingle().Subject;
        expectation.State.Should().Be(PnlExpectationState.Overdue);
        expectation.AmountEstimate.Should().BeNull();
        expectation.ClosedByUserId.Should().BeNull();
        harness.Lines.Lines[harness.OrderLineId].CompletenessState.Should().Be(PnlLineCompleteness.Overdue);
    }

    [Fact]
    public async Task RunAsync_ImmaterialClosure_IsRecordedAsASystemDecision()
    {
        // Arrange
        Harness harness = Build(5m);

        // Act
        await harness.Updater.RunAsync(CancellationToken.None);

        // Assert: no user behind it, and the reason names the automatic decision.
        PnlLineExpectation expectation = harness.Expectations.Expectations.Should().ContainSingle().Subject;
        expectation.ClosedByUserId.Should().BeNull();
        expectation.ClosureReason.Should().Be(ExpectationPolicy.ImmaterialClosureReason);
        expectation.ClosedAt.Should().Be(Now);
    }

    [Fact]
    public async Task RunAsync_NothingDue_DoesNothing()
    {
        // Arrange: the window is still open.
        Harness harness = Build(5m, expectedBy: Now.AddDays(1));

        // Act
        int examined = await harness.Updater.RunAsync(CancellationToken.None);

        // Assert
        examined.Should().Be(0);
        harness.Expectations.Expectations.Should().ContainSingle()
            .Which.State.Should().Be(PnlExpectationState.Expected);
    }

    private static Harness Build(decimal? estimate, DateTimeOffset? expectedBy = null)
    {
        Guid orderLineId = Guid.NewGuid();

        InMemoryPnlJournalStore journal = new InMemoryPnlJournalStore();
        journal.Entries.Add(SaleEntry(orderLineId));

        InMemoryOrderLineRefStore refs = new InMemoryOrderLineRefStore();
        refs.Refs[orderLineId] = Ref(orderLineId);

        InMemoryPnlExpectationStore expectations = new InMemoryPnlExpectationStore();
        expectations.Expectations.Add(new PnlLineExpectation
        {
            Id = Guid.NewGuid(),
            OrderLineId = orderLineId,
            ExpectedType = PnlEntryType.Logistics,
            Source = PnlSource.WbReport,
            ExpectedBy = expectedBy ?? Now.AddDays(-1),
            State = PnlExpectationState.Expected,
            Currency = "RUB",
            CreatedAt = Now.AddDays(-20),
            UpdatedAt = Now.AddDays(-20),
        });

        if (estimate.HasValue)
        {
            expectations.Estimates[orderLineId] = estimate.Value;
        }

        InMemoryPnlLineStore lines = new InMemoryPnlLineStore();

        PnlLineProjector projector = new PnlLineProjector(
            journal,
            refs,
            new InMemoryOrderLineCostStore(),
            expectations,
            lines,
            new FixedTimeProvider(Now),
            NullLogger<PnlLineProjector>.Instance);

        ExpectationStateUpdater updater = new ExpectationStateUpdater(
            expectations,
            projector,
            new FixedTimeProvider(Now),
            NullLogger<ExpectationStateUpdater>.Instance);

        return new Harness(updater, expectations, lines, orderLineId);
    }

    private static OrderLineRef Ref(Guid orderLineId)
    {
        return new OrderLineRef
        {
            OrderLineId = orderLineId,
            OrderId = Guid.NewGuid(),
            ChannelId = ChannelId,
            ExternalOrderId = "order-1",
            ExternalLineId = "srid-1",
            ExternalProductId = "174371959",
            Quantity = 1,
            OrderDate = Now,
            Currency = "RUB",
            UpdatedAt = Now,
        };
    }

    private static PnlEntry SaleEntry(Guid orderLineId)
    {
        return new PnlEntry
        {
            Id = Guid.NewGuid(),
            Source = PnlSource.WbReport,
            ReportInstanceKey = "799692300:2026-08-03",
            ExternalEntryId = "rrd-1",
            ChannelId = ChannelId,
            ExternalOrderId = null,
            ExternalLineId = "srid-1",
            ExternalProductId = "174371959",
            Kind = PnlEntryKind.Component,
            Type = PnlEntryType.Sale,
            Amount = 204m,
            Direction = PnlEntryDirection.Income,
            Currency = "RUB",
            DocumentDate = new DateOnly(2026, 8, 1),
            PeriodFrom = new DateOnly(2026, 7, 27),
            PeriodTo = new DateOnly(2026, 8, 2),
        };
    }

    private sealed record Harness(
        ExpectationStateUpdater Updater,
        InMemoryPnlExpectationStore Expectations,
        InMemoryPnlLineStore Lines,
        Guid OrderLineId);
}
