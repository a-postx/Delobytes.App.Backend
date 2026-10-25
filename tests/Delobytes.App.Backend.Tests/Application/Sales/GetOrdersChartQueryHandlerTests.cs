using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Sales.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Sales.Application.Models;
using Delobytes.App.Backend.Sales.Application.Queries.Orders;
using FluentAssertions;
using FluentValidation;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Sales;

/// <summary>
/// Tests for GetOrdersChartQueryHandler. Covers default resolution against the tenant's time
/// zone (not UTC), explicit versus adaptive granularity, forwarding of the channel filter and
/// the tenant currency to the repository, the UTC window handed to the repository, and the
/// validation and authorization failures required by the API contract.
/// </summary>
public class GetOrdersChartQueryHandlerTests
{
    private const string Currency = "RUB";

    // Vladivostok is UTC+10 with no DST: a UTC instant of 20:00 on 2026-01-10 is already
    // 2026-01-11 in the tenant's calendar, so a UTC-based default is caught by these tests.
    private const string TenantTimeZoneId = "Asia/Vladivostok";

    private static readonly DateTimeOffset PinnedUtcNow = new(2026, 1, 10, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TenantToday = new(2026, 1, 11);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Mock<IOrderStatsRepository> _repositoryMock = new();
    private readonly Mock<ITenantSettingsProvider> _settingsProviderMock = new();
    private readonly Mock<ITenantContext> _tenantContextMock = new();

    public GetOrdersChartQueryHandlerTests()
    {
        _tenantContextMock.Setup(t => t.TenantId).Returns(_tenantId);

        _settingsProviderMock
            .Setup(p => p.GetAccountingSettingsAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAccountingSettings { Currency = Currency, TimeZoneId = TenantTimeZoneId });

        _repositoryMock
            .Setup(r => r.GetLineFactsAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OrderLineFact>)new List<OrderLineFact>());
    }

    [Fact]
    public async Task Handle_NoDates_DefaultsToSevenTenantLocalDaysEndingToday()
    {
        // Arrange
        GetOrdersChartQueryHandler handler = BuildHandler();

        // Act
        GetOrdersChartResponse response = await handler.Handle(new GetOrdersChartQuery(), CancellationToken.None);

        // Assert: 2026-01-11 is "today" only in the tenant zone; in UTC it is still 2026-01-10.
        response.To.Should().Be(TenantToday);
        response.From.Should().Be(new DateOnly(2026, 1, 5));
        response.Points.Should().HaveCount(7);
        response.Points[0].PeriodStart.Should().Be(new DateOnly(2026, 1, 5));
        response.Points[^1].PeriodStart.Should().Be(TenantToday);
    }

    [Fact]
    public async Task Handle_OnlyToGiven_ResolvesFromAsSixDaysBeforeTo()
    {
        // Arrange: a partial request must still produce the 7-day default window.
        GetOrdersChartQueryHandler handler = BuildHandler();
        GetOrdersChartQuery query = new() { To = new DateOnly(2026, 1, 20) };

        // Act
        GetOrdersChartResponse response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.To.Should().Be(new DateOnly(2026, 1, 20));
        response.From.Should().Be(new DateOnly(2026, 1, 14));
        response.Points.Should().HaveCount(7);
    }

    [Fact]
    public async Task Handle_OnlyFromGiven_ResolvesToAsTenantToday()
    {
        // Arrange
        GetOrdersChartQueryHandler handler = BuildHandler();
        GetOrdersChartQuery query = new() { From = new DateOnly(2026, 1, 9) };

        // Act
        GetOrdersChartResponse response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.From.Should().Be(new DateOnly(2026, 1, 9));
        response.To.Should().Be(TenantToday);
    }

    [Fact]
    public async Task Handle_ExplicitGranularity_EchoesClientChoiceInsteadOfAdaptiveDefault()
    {
        // Arrange: a three-day range would adaptively resolve to Day; the client asked for Month.
        GetOrdersChartQueryHandler handler = BuildHandler();
        GetOrdersChartQuery query = new()
        {
            From = new DateOnly(2026, 1, 9),
            To = TenantToday,
            Granularity = ChartGranularity.Month,
        };

        // Act
        GetOrdersChartResponse response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.Granularity.Should().Be(ChartGranularity.Month);
        response.Points.Should().ContainSingle()
            .Which.PeriodStart.Should().Be(new DateOnly(2026, 1, 1));
    }

    [Theory]
    [InlineData(31, ChartGranularity.Day)]
    [InlineData(32, ChartGranularity.Week)]
    [InlineData(120, ChartGranularity.Week)]
    [InlineData(121, ChartGranularity.Month)]
    public async Task Handle_OmittedGranularity_FollowsAdaptiveThresholds(int rangeDays, ChartGranularity expected)
    {
        // Arrange
        GetOrdersChartQueryHandler handler = BuildHandler();
        DateOnly from = new(2026, 1, 1);
        GetOrdersChartQuery query = new() { From = from, To = from.AddDays(rangeDays - 1) };

        // Act
        GetOrdersChartResponse response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.Granularity.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_ChannelIdGiven_ForwardsItToRepositoryUnchanged()
    {
        // Arrange
        Guid channelId = Guid.NewGuid();
        GetOrdersChartQueryHandler handler = BuildHandler();
        GetOrdersChartQuery query = new() { ChannelId = channelId };

        // Act
        GetOrdersChartResponse response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.ChannelId.Should().Be(channelId);

        _repositoryMock.Verify(
            r => r.GetLineFactsAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<string>(),
                channelId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ChannelIdOmitted_ForwardsNullAndEchoesNull()
    {
        // Arrange
        GetOrdersChartQueryHandler handler = BuildHandler();

        // Act
        GetOrdersChartResponse response = await handler.Handle(new GetOrdersChartQuery(), CancellationToken.None);

        // Assert
        response.ChannelId.Should().BeNull();

        _repositoryMock.Verify(
            r => r.GetLineFactsAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<string>(),
                (Guid?)null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_TenantTimeZoneIsNotUtc_ConvertsWindowBoundariesThroughThatZone()
    {
        // Arrange: 2026-01-05..2026-01-11 local in UTC+10 is 2026-01-04T14:00Z through
        // 2026-01-11T14:00Z. A naive "UTC midnight + 1 day" exclusive bound would be 2026-01-12T00:00Z.
        GetOrdersChartQueryHandler handler = BuildHandler();

        // Act
        await handler.Handle(new GetOrdersChartQuery(), CancellationToken.None);

        // Assert
        DateTimeOffset expectedFromUtc = new(2026, 1, 4, 14, 0, 0, TimeSpan.Zero);
        DateTimeOffset expectedToUtcExclusive = new(2026, 1, 11, 14, 0, 0, TimeSpan.Zero);

        _repositoryMock.Verify(
            r => r.GetLineFactsAsync(
                expectedFromUtc,
                expectedToUtcExclusive,
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ExplicitDatesGiven_UsesThemWithoutShiftingToTenantToday()
    {
        // Arrange
        GetOrdersChartQueryHandler handler = BuildHandler();
        GetOrdersChartQuery query = new()
        {
            From = new DateOnly(2025, 3, 1),
            To = new DateOnly(2025, 3, 31),
        };

        // Act
        GetOrdersChartResponse response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.From.Should().Be(new DateOnly(2025, 3, 1));
        response.To.Should().Be(new DateOnly(2025, 3, 31));
        response.Points.Should().HaveCount(31);
    }

    [Fact]
    public async Task Handle_RangeOfExactly366Days_IsAccepted()
    {
        // Arrange: the upper bound of the allowed range must not be rejected.
        GetOrdersChartQueryHandler handler = BuildHandler();
        DateOnly from = new(2026, 1, 1);
        GetOrdersChartQuery query = new() { From = from, To = from.AddDays(365) };

        // Act
        GetOrdersChartResponse response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.To.Should().Be(from.AddDays(365));
    }

    [Fact]
    public async Task Handle_ToEarlierThanFrom_ThrowsValidationExceptionOnToField()
    {
        // Arrange
        GetOrdersChartQueryHandler handler = BuildHandler();
        GetOrdersChartQuery query = new()
        {
            From = new DateOnly(2026, 1, 10),
            To = new DateOnly(2026, 1, 5),
        };

        // Act
        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(
            () => handler.Handle(query, CancellationToken.None));

        // Assert
        exception.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be(nameof(GetOrdersChartQuery.To));
    }

    [Fact]
    public async Task Handle_RangeOf367Days_ThrowsValidationExceptionOnToField()
    {
        // Arrange
        GetOrdersChartQueryHandler handler = BuildHandler();
        DateOnly from = new(2026, 1, 1);
        GetOrdersChartQuery query = new() { From = from, To = from.AddDays(366) };

        // Act
        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(
            () => handler.Handle(query, CancellationToken.None));

        // Assert
        exception.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be(nameof(GetOrdersChartQuery.To));
    }

    [Fact]
    public async Task Handle_InvalidRange_DoesNotQueryTheRepository()
    {
        // Arrange: validation must fail closed, before any money-bearing read.
        GetOrdersChartQueryHandler handler = BuildHandler();
        GetOrdersChartQuery query = new()
        {
            From = new DateOnly(2026, 1, 10),
            To = new DateOnly(2026, 1, 5),
        };

        // Act
        Func<Task> act = () => handler.Handle(query, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();

        // Assert
        _repositoryMock.Verify(
            r => r.GetLineFactsAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_MissingTenant_ThrowsUnauthorizedAccessException()
    {
        // Arrange: no tenant in the context means no accounting settings can be resolved either.
        _tenantContextMock.Setup(t => t.TenantId).Returns((Guid?)null);
        GetOrdersChartQueryHandler handler = BuildHandler();

        // Act
        Func<Task> act = () => handler.Handle(new GetOrdersChartQuery(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        _settingsProviderMock.Verify(
            p => p.GetAccountingSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownTenantTimeZone_ThrowsInvalidOperationException()
    {
        // Arrange: an unresolvable zone must surface as a server error, not silently fall back to UTC.
        _settingsProviderMock
            .Setup(p => p.GetAccountingSettingsAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAccountingSettings { Currency = Currency, TimeZoneId = "Not/AZone" });

        GetOrdersChartQueryHandler handler = BuildHandler();

        // Act
        Func<Task> act = () => handler.Handle(new GetOrdersChartQuery(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Not/AZone*");
    }

    [Fact]
    public async Task Handle_TenantCurrencyGiven_PassesItToRepositoryAndEchoesIt()
    {
        // Arrange
        _settingsProviderMock
            .Setup(p => p.GetAccountingSettingsAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAccountingSettings { Currency = "USD", TimeZoneId = TenantTimeZoneId });

        GetOrdersChartQueryHandler handler = BuildHandler();

        // Act
        GetOrdersChartResponse response = await handler.Handle(new GetOrdersChartQuery(), CancellationToken.None);

        // Assert
        response.Currency.Should().Be("USD");

        _repositoryMock.Verify(
            r => r.GetLineFactsAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                "USD",
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NoFacts_ReturnsZeroFilledSeriesAndZeroTotals()
    {
        // Arrange: an empty period is not an error, and the totals must not leak defaults.
        GetOrdersChartQueryHandler handler = BuildHandler();

        // Act
        GetOrdersChartResponse response = await handler.Handle(new GetOrdersChartQuery(), CancellationToken.None);

        // Assert
        response.Points.Should().OnlyContain(p => p.Quantity == 0 && p.Amount == 0m && p.OrderCount == 0);
        response.Totals.Quantity.Should().Be(0);
        response.Totals.Amount.Should().Be(0m);
        response.Totals.OrderCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_FactsGiven_BucketsInTenantLocalDaysAndSumsTotals()
    {
        // Arrange: 2026-01-05T23:30Z is 2026-01-06 in UTC+10, so the quantity must land on the
        // second point, not the first; 2 x 100.50 makes the day total 201.00.
        List<OrderLineFact> facts = new()
        {
            new OrderLineFact
            {
                OrderId = Guid.NewGuid(),
                OrderDate = new DateTimeOffset(2026, 1, 5, 23, 30, 0, TimeSpan.Zero),
                Quantity = 2,
                UnitPrice = 100.50m,
            },
            new OrderLineFact
            {
                OrderId = Guid.NewGuid(),
                OrderDate = new DateTimeOffset(2026, 1, 7, 23, 30, 0, TimeSpan.Zero),
                Quantity = 1,
                UnitPrice = null,
            },
        };

        _repositoryMock
            .Setup(r => r.GetLineFactsAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OrderLineFact>)facts);

        GetOrdersChartQueryHandler handler = BuildHandler();

        // Act
        GetOrdersChartResponse response = await handler.Handle(new GetOrdersChartQuery(), CancellationToken.None);

        // Assert
        response.Points.Should().HaveCount(7);
        response.Points[0].PeriodStart.Should().Be(new DateOnly(2026, 1, 5));
        response.Points[0].Quantity.Should().Be(0);
        response.Points[1].PeriodStart.Should().Be(new DateOnly(2026, 1, 6));
        response.Points[1].Quantity.Should().Be(2);
        response.Points[1].Amount.Should().Be(201.00m);
        response.Points[1].OrderCount.Should().Be(1);

        // The null-priced unit still counts as ordered; it contributes nothing to the amount.
        response.Points[3].PeriodStart.Should().Be(new DateOnly(2026, 1, 8));
        response.Points[3].Quantity.Should().Be(1);
        response.Points[3].Amount.Should().Be(0m);
        response.Points[3].OrderCount.Should().Be(1);

        response.Totals.Quantity.Should().Be(3);
        response.Totals.Amount.Should().Be(201.00m);
        response.Totals.OrderCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_MultipleLinesSameOrder_CountsOrderOnceInTotals()
    {
        // Arrange: two lines of the same order must contribute a single order to the totals,
        // even though they add to quantity and amount separately.
        Guid orderId = Guid.NewGuid();
        DateTimeOffset orderDate = new(2026, 1, 6, 10, 0, 0, TimeSpan.Zero);
        List<OrderLineFact> facts = new()
        {
            new OrderLineFact { OrderId = orderId, OrderDate = orderDate, Quantity = 1, UnitPrice = 100m },
            new OrderLineFact { OrderId = orderId, OrderDate = orderDate, Quantity = 1, UnitPrice = 50m },
        };

        _repositoryMock
            .Setup(r => r.GetLineFactsAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OrderLineFact>)facts);

        GetOrdersChartQueryHandler handler = BuildHandler();

        // Act
        GetOrdersChartResponse response = await handler.Handle(new GetOrdersChartQuery(), CancellationToken.None);

        // Assert
        response.Totals.Quantity.Should().Be(2);
        response.Totals.Amount.Should().Be(150m);
        response.Totals.OrderCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WeekGranularityGiven_RollsDaysUpToMondayBuckets()
    {
        // Arrange: 2026-01-05 is a Monday; the range 2026-01-08..2026-01-11 sits inside one week.
        GetOrdersChartQueryHandler handler = BuildHandler();
        GetOrdersChartQuery query = new()
        {
            From = new DateOnly(2026, 1, 8),
            To = TenantToday,
            Granularity = ChartGranularity.Week,
        };

        // Act
        GetOrdersChartResponse response = await handler.Handle(query, CancellationToken.None);

        // Assert: a partial bucket is allowed and starts before the requested from.
        response.Points.Should().ContainSingle()
            .Which.PeriodStart.Should().Be(new DateOnly(2026, 1, 5));
    }

    private GetOrdersChartQueryHandler BuildHandler()
    {
        return new GetOrdersChartQueryHandler(
            _repositoryMock.Object,
            _settingsProviderMock.Object,
            _tenantContextMock.Object,
            new FixedTimeProvider(PinnedUtcNow));
    }

    /// <summary>
    /// Pins the clock so the handler's "today" resolution is deterministic.
    /// </summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
