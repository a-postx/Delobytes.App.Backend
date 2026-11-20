using System;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// Tests for the P&amp;L read-model formulas: each VAT mode, a missing profile, and the null and
/// zero-denominator guards. Tax is computed at read time because the profile is versioned.
/// </summary>
public class PnlTaxCalculatorTests
{
    [Theory]
    [InlineData(VatType.None, 0)]
    [InlineData(VatType.Five, 5)]
    [InlineData(VatType.Seven, 7)]
    [InlineData(VatType.TwentyTwo, 22)]
    public void VatRate_ReturnsTheRateOfTheMode(VatType vat, int expected)
    {
        PnlTaxCalculator.VatRate(vat).Should().Be(expected);
    }

    [Theory]
    [InlineData(VatType.None, 0)]
    [InlineData(VatType.Five, 4.7619047619047619047619047619)]
    [InlineData(VatType.Seven, 6.5420560747663551401869158879)]
    [InlineData(VatType.TwentyTwo, 18.032786885245901639344262295)]
    public void EstimatedVat_IsTheContainedShareOfTheGrossBase(VatType vat, decimal expected)
    {
        decimal? vatAmount = PnlTaxCalculator.EstimatedVat(100m, vat);

        vatAmount.Should().BeApproximately(expected, 0.000000000001m);
    }

    [Fact]
    public void EstimatedTax_WithoutAProfile_IsNull()
    {
        PnlTaxCalculator.EstimatedTax(100m, null).Should().BeNull();
    }

    [Fact]
    public void EstimatedTax_WithoutABase_IsNull()
    {
        TenantTaxProfileSnapshot profile = new TenantTaxProfileSnapshot
        {
            Regime = TaxRegime.UsnIncome,
            RatePercent = 6m,
            Vat = VatType.None,
        };

        PnlTaxCalculator.EstimatedTax(null, profile).Should().BeNull();
    }

    [Fact]
    public void EstimatedTax_IsChargedOnTheBaseWithoutVat()
    {
        TenantTaxProfileSnapshot profile = new TenantTaxProfileSnapshot
        {
            Regime = TaxRegime.UsnIncome,
            RatePercent = 6m,
            Vat = VatType.Five,
        };

        // 100 gross, 4.7619 VAT, 6% of 95.2381.
        PnlTaxCalculator.EstimatedTax(100m, profile)
            .Should().BeApproximately(5.7142857142857142857142857142m, 0.000000000001m);
    }

    [Fact]
    public void MarketplaceFee_IsPositiveWhenTheChannelCharged()
    {
        PnlTaxCalculator.MarketplaceFee(-800m, -8m, -150m).Should().Be(958m);
    }

    [Fact]
    public void MarketplaceFee_IsNullWithoutAnyComponent()
    {
        PnlTaxCalculator.MarketplaceFee(null, null, null).Should().BeNull();
    }

    [Fact]
    public void Margin_RequiresRevenueAndACompleteCost()
    {
        PnlTaxCalculator.Margin(1000m, 400m, true).Should().Be(600m);
        PnlTaxCalculator.Margin(1000m, null, true).Should().BeNull();
        PnlTaxCalculator.Margin(null, 400m, true).Should().BeNull();
        PnlTaxCalculator.Margin(1000m, 400m, false).Should().BeNull();
        PnlTaxCalculator.Margin(1000m, 400m, null).Should().BeNull();
    }

    [Fact]
    public void LineMargin_WithoutASale_IsTheNetRevenueItself()
    {
        // No unit left the seller, so no cost of goods applies: the negative result is known.
        PnlTaxCalculator.LineMargin(-53.84m, null, null, hasSale: false).Should().Be(-53.84m);
        PnlTaxCalculator.LineMargin(null, null, null, hasSale: false).Should().BeNull();
    }

    [Fact]
    public void LineMargin_WithASale_KeepsRequiringACompleteCost()
    {
        PnlTaxCalculator.LineMargin(1000m, 400m, true, hasSale: true).Should().Be(600m);
        PnlTaxCalculator.LineMargin(1000m, null, null, hasSale: true).Should().BeNull();
        PnlTaxCalculator.LineMargin(1000m, 400m, false, hasSale: true).Should().BeNull();
    }

    [Fact]
    public void TaxAdjustedMargin_IsOptionalAndDoesNotGateThePreTaxMargin()
    {
        PnlTaxCalculator.TaxAdjustedMargin(600m, 36m).Should().Be(564m);
        PnlTaxCalculator.TaxAdjustedMargin(600m, null).Should().BeNull();
        PnlTaxCalculator.TaxAdjustedMargin(null, 36m).Should().BeNull();
    }

    [Fact]
    public void MarginPercent_AndRoi_GuardTheirDenominators()
    {
        PnlTaxCalculator.MarginPercent(50m, 1000m).Should().Be(0.05m);
        PnlTaxCalculator.MarginPercent(50m, null).Should().BeNull();
        PnlTaxCalculator.MarginPercent(50m, 0m).Should().BeNull();

        PnlTaxCalculator.Roi(50m, 200m).Should().Be(0.25m);
        PnlTaxCalculator.Roi(50m, null).Should().BeNull();
        PnlTaxCalculator.Roi(50m, 0m).Should().BeNull();
    }
}
