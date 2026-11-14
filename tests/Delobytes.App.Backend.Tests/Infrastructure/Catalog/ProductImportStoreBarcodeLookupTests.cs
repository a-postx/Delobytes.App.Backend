using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Catalog;

/// <summary>
/// The two lookups the import added to <see cref="ProductImportStore"/>. Both have to see rows staged
/// earlier in the same batch, not only committed ones: cards are saved one at a time, so a sibling
/// card's link or barcode is invisible to a query until the batch commits. Reading the change
/// tracker as well is what keeps a batch from disagreeing with itself.
/// </summary>
public class ProductImportStoreBarcodeLookupTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _channelId = Guid.NewGuid();
    private readonly Guid _otherChannelId = Guid.NewGuid();

    [Fact]
    public async Task FindProductsByChannelBarcodes_IgnoresCardsOfOtherChannels()
    {
        // Arrange: the same value is reported by a card of another channel. It says nothing about
        // this channel's cards, so it must not produce a match here.
        string databaseName = Guid.NewGuid().ToString();

        Guid otherProductId = Guid.NewGuid();

        using (CatalogDbContext seed = BuildContext(databaseName))
        {
            Product otherProduct = BuildProduct(otherProductId, "P-000001", "SKU-OTHER");
            ChannelProduct otherLink = BuildLink(otherProductId, _otherChannelId, "other-card");
            otherLink.Barcodes.Add(BuildChannelBarcode(otherLink.Id, "4600000000009"));
            otherProduct.ChannelProducts.Add(otherLink);

            seed.Products.Add(otherProduct);
            await seed.SaveChangesAsync();
        }

        using CatalogDbContext context = BuildContext(databaseName);
        ProductImportStore store = new ProductImportStore(context);

        // Act
        IReadOnlyList<Product> products = await store.FindProductsByChannelBarcodesAsync(
            _channelId,
            new[] { "4600000000009" },
            CancellationToken.None);

        // Assert
        products.Should().BeEmpty("the value belongs to another channel, not to this one");
    }

    [Fact]
    public async Task FindProductsByChannelBarcodes_FindsCommittedRows()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        Guid productId = Guid.NewGuid();

        using (CatalogDbContext seed = BuildContext(databaseName))
        {
            Product product = BuildProduct(productId, "P-000002", "SKU-ONE");
            ChannelProduct link = BuildLink(productId, _channelId, "card-1");
            link.Barcodes.Add(BuildChannelBarcode(link.Id, "4600000000009"));
            product.ChannelProducts.Add(link);

            seed.Products.Add(product);
            await seed.SaveChangesAsync();
        }

        using CatalogDbContext context = BuildContext(databaseName);
        ProductImportStore store = new ProductImportStore(context);

        // Act
        IReadOnlyList<Product> products = await store.FindProductsByChannelBarcodesAsync(
            _channelId,
            new[] { "4600000000009" },
            CancellationToken.None);

        // Assert
        products.Should().ContainSingle().Which.Id.Should().Be(productId);
    }

    [Fact]
    public async Task FindProductsByChannelBarcodes_SeesBarcodesStagedInTheSameBatch()
    {
        // Arrange: the card was processed earlier in this batch and is not committed yet, so only
        // the change tracker knows about its barcode.
        using CatalogDbContext context = BuildContext(Guid.NewGuid().ToString());
        ProductImportStore store = new ProductImportStore(context);

        Product stagedProduct = BuildProduct(Guid.NewGuid(), "P-000003", "SKU-STAGED");
        ChannelProduct stagedLink = BuildLink(stagedProduct.Id, _channelId, "card-staged");

        store.AddProduct(stagedProduct);
        store.AddChannelProduct(stagedLink);
        store.AddChannelProductBarcode(BuildChannelBarcode(stagedLink.Id, "4600000000009"));

        // Act
        IReadOnlyList<Product> products = await store.FindProductsByChannelBarcodesAsync(
            _channelId,
            new[] { "4600000000009" },
            CancellationToken.None);

        // Assert
        products.Should().ContainSingle().Which.Id.Should().Be(stagedProduct.Id);
    }

    [Fact]
    public async Task FindTakenCanonicalBarcodes_ReportsOnlyValuesOwnedByOtherProducts()
    {
        // Arrange: one value is free, one belongs to another product, one belongs to the product
        // being asked about (and is therefore not "taken" from its own perspective).
        string databaseName = Guid.NewGuid().ToString();

        Guid ownProductId = Guid.NewGuid();

        using (CatalogDbContext seed = BuildContext(databaseName))
        {
            Product own = BuildProduct(ownProductId, "P-000004", "SKU-OWN");
            own.Barcodes.Add(BuildCanonicalBarcode(ownProductId, "1111111111111"));

            Guid foreignProductId = Guid.NewGuid();
            Product foreign = BuildProduct(foreignProductId, "P-000005", "SKU-FOREIGN");
            foreign.Barcodes.Add(BuildCanonicalBarcode(foreignProductId, "2222222222222"));

            seed.Products.AddRange(own, foreign);
            await seed.SaveChangesAsync();
        }

        using CatalogDbContext context = BuildContext(databaseName);
        ProductImportStore store = new ProductImportStore(context);

        // Act
        IReadOnlySet<string> taken = await store.FindTakenCanonicalBarcodesAsync(
            new[] { "1111111111111", "2222222222222", "3333333333333" },
            ownProductId,
            CancellationToken.None);

        // Assert
        taken.Should().BeEquivalentTo(new[] { "2222222222222" });
    }

    [Fact]
    public async Task FindTakenCanonicalBarcodes_SeesValuesStagedInTheSameBatch()
    {
        // Arrange: another card of this batch already claimed the value canonically. It is not in the
        // database yet, but it will be at SaveChanges time, where the unique index sees it.
        using CatalogDbContext context = BuildContext(Guid.NewGuid().ToString());
        ProductImportStore store = new ProductImportStore(context);

        Product stagedProduct = BuildProduct(Guid.NewGuid(), "P-000006", "SKU-STAGED");
        store.AddProduct(stagedProduct);
        store.AddProductBarcode(BuildCanonicalBarcode(stagedProduct.Id, "4600000000009"));

        // Act
        IReadOnlySet<string> taken = await store.FindTakenCanonicalBarcodesAsync(
            new[] { "4600000000009" },
            Guid.NewGuid(),
            CancellationToken.None);

        // Assert
        taken.Should().BeEquivalentTo(new[] { "4600000000009" });
    }

    [Fact]
    public async Task FindTakenCanonicalBarcodes_EmptyInput_QueriesNothing()
    {
        using CatalogDbContext context = BuildContext(Guid.NewGuid().ToString());
        ProductImportStore store = new ProductImportStore(context);

        IReadOnlySet<string> taken = await store.FindTakenCanonicalBarcodesAsync(
            Array.Empty<string>(),
            Guid.NewGuid(),
            CancellationToken.None);

        taken.Should().BeEmpty();
    }

    private static Product BuildProduct(Guid productId, string code, string sku)
    {
        return new Product
        {
            Id = productId,
            Code = code,
            Sku = sku,
            Name = "Product " + code,
            Status = ProductStatus.Active,
            CreationSource = CreationSource.Manual,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
    }

    private static ChannelProduct BuildLink(Guid productId, Guid channelId, string externalProductId)
    {
        return new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ChannelId = channelId,
            ExternalProductId = externalProductId,
            ExternalSku = "ART-" + externalProductId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
    }

    private static ProductBarcode BuildCanonicalBarcode(Guid productId, string value)
    {
        return new ProductBarcode
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Value = value,
            Type = ChannelCodes.Wildberries,
            IsDefault = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
    }

    private static ChannelProductBarcode BuildChannelBarcode(Guid channelProductId, string value)
    {
        return new ChannelProductBarcode
        {
            Id = Guid.NewGuid(),
            ChannelProductId = channelProductId,
            Value = value,
            Type = ChannelCodes.Wildberries,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
    }

    private CatalogDbContext BuildContext(string databaseName)
    {
        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(context => context.TenantId).Returns(_tenantId);

        return new CatalogDbContext(options, tenantContextMock.Object);
    }
}
