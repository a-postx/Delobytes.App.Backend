using System;
using System.Collections.Generic;
using System.Linq;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Contracts.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Catalog;

/// <summary>
/// Model metadata for the canonical and per-channel packing units. The two unique indexes are the
/// only enforcement of "at most one row" for each: PostgreSQL treats NULL values as distinct, so
/// the composite index cannot bound the canonical row and a filtered index is required.
/// </summary>
public class PackingUnitConfigurationTests
{
    private const string CanonicalFilter = "\"ChannelId\" IS NULL";

    [Fact]
    public void ProductAndChannel_HaveAUniqueCompositeIndex()
    {
        using CatalogDbContext context = BuildContext();

        IIndex index = GetPackingUnitIndexes(context)
            .Single(candidate => candidate.GetDatabaseName() == "IX_PackingUnits_Product_Channel");

        index.IsUnique.Should().BeTrue();
        index.Properties.Select(property => property.Name)
            .Should().Equal("ProductId", "ChannelId");
    }

    [Fact]
    public void CanonicalUnit_IsBoundedByAFilteredUniqueIndex()
    {
        using CatalogDbContext context = BuildContext();

        IIndex index = GetPackingUnitIndexes(context)
            .Single(candidate => candidate.GetDatabaseName() == "IX_PackingUnits_Product_Canonical");

        index.IsUnique.Should().BeTrue();
        index.Properties.Select(property => property.Name).Should().Equal("ProductId");
        index.GetFilter().Should().Be(CanonicalFilter);
    }

    [Fact]
    public void SingleColumnProductIdIndex_IsGone_BecauseTheCompositeIndexCoversIt()
    {
        // IX_PackingUnits_ProductId is replaced by IX_PackingUnits_Product_Channel: ProductId is
        // its leading column, so a second index on the same column only doubles the write cost.
        using CatalogDbContext context = BuildContext();

        GetPackingUnitIndexes(context)
            .Should().NotContain(candidate => candidate.GetDatabaseName() == "IX_PackingUnits_ProductId");
    }

    [Fact]
    public void ChannelId_HasACascadingForeignKeyToChannels()
    {
        using CatalogDbContext context = BuildContext();

        IEntityType packingUnitType = context.Model.FindEntityType(typeof(PackingUnit))!;
        IForeignKey foreignKey = packingUnitType.GetForeignKeys()
            .Single(candidate => candidate.Properties.Single().Name == nameof(PackingUnit.ChannelId));

        foreignKey.PrincipalEntityType.ClrType.Should().Be(typeof(Channel));
        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Cascade);
    }

    private static IEnumerable<IIndex> GetPackingUnitIndexes(CatalogDbContext context)
    {
        IEntityType packingUnitType = context.Model.FindEntityType(typeof(PackingUnit))!;
        return packingUnitType.GetIndexes();
    }

    private static CatalogDbContext BuildContext()
    {
        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("packing-unit-configuration-" + Guid.NewGuid())
            .Options;

        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.TenantId).Returns(Guid.NewGuid());

        return new CatalogDbContext(options, tenantContextMock.Object);
    }
}
