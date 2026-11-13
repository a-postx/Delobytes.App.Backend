using System;
using Delobytes.App.Backend.Catalog.Application.Packing;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog.Packing;

/// <summary>
/// The resolver is the single place that decides which packing unit is canonical and which one
/// belongs to a channel. Both answers may legitimately be "none": a product can exist without any
/// dimensions, and every caller must handle that instead of dereferencing a row.
/// </summary>
public class PackingUnitResolverTests
{
    private readonly Guid _channelId = Guid.NewGuid();
    private readonly Guid _otherChannelId = Guid.NewGuid();

    [Fact]
    public void ResolveCanonical_ReturnsTheRowWithoutChannel_AndIgnoresChannelRows()
    {
        Product product = BuildProduct();
        PackingUnit canonical = AddUnit(product, channelId: null);
        AddUnit(product, _channelId);

        PackingUnit? resolved = PackingUnitResolver.ResolveCanonical(product);

        resolved.Should().BeSameAs(canonical);
    }

    [Fact]
    public void ResolveCanonical_ReturnsNull_WhenOnlyChannelRowsExist()
    {
        Product product = BuildProduct();
        AddUnit(product, _channelId);
        AddUnit(product, _otherChannelId);

        PackingUnitResolver.ResolveCanonical(product).Should().BeNull();
    }

    [Fact]
    public void ResolveForChannel_ReturnsOnlyTheRequestedChannelsRow()
    {
        Product product = BuildProduct();
        AddUnit(product, channelId: null);
        PackingUnit requested = AddUnit(product, _channelId);
        AddUnit(product, _otherChannelId);

        PackingUnit? resolved = PackingUnitResolver.ResolveForChannel(product, _channelId);

        resolved.Should().BeSameAs(requested);
    }

    [Fact]
    public void ResolveForChannel_ReturnsNull_ForAnotherChannel()
    {
        Product product = BuildProduct();
        AddUnit(product, _otherChannelId);

        PackingUnitResolver.ResolveForChannel(product, _channelId).Should().BeNull();
    }

    [Fact]
    public void BothMethods_IgnoreInactiveRows()
    {
        Product product = BuildProduct();
        AddUnit(product, channelId: null, isActive: false);
        AddUnit(product, _channelId, isActive: false);

        PackingUnitResolver.ResolveCanonical(product).Should().BeNull();
        PackingUnitResolver.ResolveForChannel(product, _channelId).Should().BeNull();
    }

    private static Product BuildProduct()
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            Code = "P-000001",
            Sku = "SKU-001",
            Name = "Product",
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static PackingUnit AddUnit(Product product, Guid? channelId, bool isActive = true)
    {
        PackingUnit unit = new PackingUnit
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = channelId,
            LengthCm = 10m,
            WidthCm = 10m,
            HeightCm = 10m,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        product.PackingUnits.Add(unit);

        return unit;
    }
}
