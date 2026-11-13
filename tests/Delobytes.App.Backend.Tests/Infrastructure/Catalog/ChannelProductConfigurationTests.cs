using System;
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
/// Model metadata for the channel-owned fields of ChannelProduct. Uniqueness is asserted through
/// the model, not by inserting duplicates: EF InMemory does not enforce unique indexes.
/// </summary>
public class ChannelProductConfigurationTests
{
    [Fact]
    public void NameAndDescription_ExistWithChannelColumnLimits()
    {
        using CatalogDbContext context = BuildContext();

        IEntityType channelProductType = context.Model.FindEntityType(typeof(ChannelProduct))!;

        IProperty name = channelProductType.FindProperty(nameof(ChannelProduct.Name))!;
        name.GetMaxLength().Should().Be(2000);
        name.IsNullable.Should().BeTrue();

        IProperty description = channelProductType.FindProperty(nameof(ChannelProduct.Description))!;
        description.GetMaxLength().Should().Be(20000);
        description.IsNullable.Should().BeTrue();
    }

    [Fact]
    public void ProductIdAndChannelId_HaveAUniqueIndex()
    {
        using CatalogDbContext context = BuildContext();

        IEntityType channelProductType = context.Model.FindEntityType(typeof(ChannelProduct))!;
        IIndex index = channelProductType.GetIndexes()
            .Single(candidate => candidate.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { "ProductId", "ChannelId" }));

        index.IsUnique.Should().BeTrue();
    }

    [Fact]
    public void ExternalIdentifiers_KeepTheirColumnLimits()
    {
        using CatalogDbContext context = BuildContext();

        IEntityType channelProductType = context.Model.FindEntityType(typeof(ChannelProduct))!;

        IProperty externalProductId = channelProductType.FindProperty(nameof(ChannelProduct.ExternalProductId))!;
        externalProductId.IsNullable.Should().BeFalse();
        externalProductId.GetMaxLength().Should().Be(200);

        IProperty externalSku = channelProductType.FindProperty(nameof(ChannelProduct.ExternalSku))!;
        externalSku.GetMaxLength().Should().Be(100);
    }

    private static CatalogDbContext BuildContext()
    {
        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("channel-product-configuration-" + Guid.NewGuid())
            .Options;

        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.TenantId).Returns(Guid.NewGuid());

        return new CatalogDbContext(options, tenantContextMock.Object);
    }
}
