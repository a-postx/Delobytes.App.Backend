using System;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

/// <summary>
/// Volume is the value that selects a TariffGridEntry bracket, so the rounding mode is
/// part of the pricing contract, not a formatting detail. These tests pin the exact
/// boundary behaviour: an exact .xxx5 midpoint must round away from zero.
/// </summary>
public class PackingUnitVolumeTests
{
    private static PackingUnit BuildPackingUnit(decimal lengthCm, decimal widthCm, decimal heightCm)
    {
        return new PackingUnit
        {
            Id = Guid.NewGuid(),
            Name = "Коробка",
            LengthCm = lengthCm,
            WidthCm = widthCm,
            HeightCm = heightCm,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public void GetVolumeLiters_MidpointWithEvenDigitBefore_IsRoundedAwayFromZero()
    {
        // 1.05 * 1.6 * 6.25 = 10.5 cm³ = 0.0105 l exactly.
        // MidpointRounding.ToEven (the default) would give 0.010 and lose the boundary;
        // AwayFromZero gives 0.011, which is what the tariff bracket lookup must see.
        PackingUnit unit = BuildPackingUnit(1.05m, 1.6m, 6.25m);

        decimal volume = unit.GetVolumeLiters();

        volume.Should().Be(0.011m);
    }

    [Fact]
    public void GetVolumeLiters_MidpointWithOddDigitBefore_IsRoundedAwayFromZero()
    {
        // 1.05 * 4.8 * 18.75 = 94.5 cm³ = 0.0945 l exactly.
        // ToEven would round this one up to 0.094 only by accident of the digit;
        // AwayFromZero makes both midpoints behave identically.
        PackingUnit unit = BuildPackingUnit(1.05m, 4.8m, 18.75m);

        decimal volume = unit.GetVolumeLiters();

        volume.Should().Be(0.095m);
    }

    [Fact]
    public void GetVolumeLiters_TypicalBox_IsCubicCentimetresOverOneThousand()
    {
        // 40 * 30 * 20 = 24000 cm³ = 24 l.
        PackingUnit unit = BuildPackingUnit(40m, 30m, 20m);

        decimal volume = unit.GetVolumeLiters();

        volume.Should().Be(24m);
    }

    [Fact]
    public void GetVolumeLiters_Result_IsLimitedToThreeDecimals()
    {
        // 17 * 23 * 11 = 4301 cm³ = 4.301 l; a value with more input decimals must not
        // produce more output decimals, or the comparison with VolumeThresholdLiters(10,3) drifts.
        PackingUnit unit = BuildPackingUnit(17.3m, 23.7m, 11.1m);

        decimal volume = unit.GetVolumeLiters();

        volume.Should().Be(4.551m);
    }
}
