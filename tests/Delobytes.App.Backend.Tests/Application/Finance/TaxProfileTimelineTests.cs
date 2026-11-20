using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// The timeline resolves a profile per calendar date from one load of the tenant's history. A
/// report over years must pick the same profile the per-date query would have returned.
/// </summary>
public class TaxProfileTimelineTests
{
    private static readonly DateOnly Start = new(2025, 1, 1);
    private static readonly DateOnly Change = new(2026, 1, 1);

    [Fact]
    public void Resolve_DateBeforeEveryProfile_ReturnsNull()
    {
        TaxProfileTimeline timeline = new(BuildHistory());

        timeline.Resolve(Start.AddDays(-1)).Should().BeNull();
    }

    [Fact]
    public void Resolve_ExactlyOnValidFrom_ReturnsThatProfile()
    {
        TaxProfileTimeline timeline = new(BuildHistory());

        timeline.Resolve(Change)!.RatePercent.Should().Be(15m);
    }

    [Fact]
    public void Resolve_BetweenVersions_ReturnsTheLastEffectiveProfile()
    {
        TaxProfileTimeline timeline = new(BuildHistory());

        timeline.Resolve(new DateOnly(2025, 7, 15))!.RatePercent.Should().Be(6m);
    }

    [Fact]
    public void Resolve_AfterTheLastVersion_ReturnsTheLatestProfile()
    {
        TaxProfileTimeline timeline = new(BuildHistory());

        timeline.Resolve(new DateOnly(2030, 5, 1))!.RatePercent.Should().Be(15m);
    }

    [Fact]
    public void Resolve_UnsortedHistory_StillPicksTheLatestEffectiveProfile()
    {
        TaxProfileTimeline timeline = new(BuildHistory().Reverse().ToList());

        timeline.Resolve(new DateOnly(2025, 7, 15))!.RatePercent.Should().Be(6m);
        timeline.Resolve(new DateOnly(2026, 7, 15))!.RatePercent.Should().Be(15m);
    }

    [Fact]
    public void Resolve_EmptyHistory_ReturnsNull()
    {
        TaxProfileTimeline timeline = new(Array.Empty<TenantTaxProfileSnapshot>());

        timeline.Resolve(Change).Should().BeNull();
    }

    private static IReadOnlyList<TenantTaxProfileSnapshot> BuildHistory()
    {
        return new List<TenantTaxProfileSnapshot>
        {
            new()
            {
                Regime = TaxRegime.UsnIncome,
                RatePercent = 6m,
                Vat = VatType.None,
                ValidFrom = Start,
            },
            new()
            {
                Regime = TaxRegime.UsnIncome,
                RatePercent = 15m,
                Vat = VatType.None,
                ValidFrom = Change,
            },
        };
    }
}
