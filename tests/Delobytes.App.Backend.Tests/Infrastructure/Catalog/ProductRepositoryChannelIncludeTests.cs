using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;
using Delobytes.App.Backend.Contracts.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Catalog;

/// <summary>
/// ProductRepository must eagerly load ChannelProducts.Channel through a single fixed
/// Include chain (no per-call Select projections that would hide an N+1), so query handlers
/// can read cp.Channel.Name/Code without a second round-trip per product.
/// </summary>
public class ProductRepositoryChannelIncludeTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task GetByIdAsync_LoadsChannelProductsWithChannelNavigation()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Channel channel = new Channel
        {
            Id = Guid.NewGuid(),
            Name = "Wildberries",
            Code = "wildberries",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Product product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-001",
            Name = "Test product",
            Status = ProductStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        product.ChannelProducts.Add(new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ChannelId = channel.Id,
            ExternalProductId = "123456789",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        setupContext.Channels.Add(channel);
        setupContext.Products.Add(product);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ProductRepository repository = new ProductRepository(readContext);

        // Act
        Product? loaded = await repository.GetByIdAsync(product.Id, CancellationToken.None);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.ChannelProducts.Should().ContainSingle();
        ChannelProduct loadedLink = loaded.ChannelProducts.First();
        loadedLink.Channel.Should().NotBeNull("the handler maps cp.Channel.Name/Code and must not hit a lazy-load exception");
        loadedLink.Channel.Name.Should().Be("Wildberries");
        loadedLink.Channel.Code.Should().Be("wildberries");
    }

    [Fact]
    public async Task GetAllByStatusAsync_LoadsChannelProductsWithChannelNavigation_ForEveryProduct()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Channel wbChannel = new Channel { Id = Guid.NewGuid(), Name = "Wildberries", Code = "wildberries", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        Channel ozonChannel = new Channel { Id = Guid.NewGuid(), Name = "Ozon", Code = "ozon", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };

        Product linkedProduct = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-001",
            Name = "Linked product",
            Status = ProductStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        linkedProduct.ChannelProducts.Add(new ChannelProduct
        {
            Id = Guid.NewGuid(),
            ProductId = linkedProduct.Id,
            ChannelId = wbChannel.Id,
            ExternalProductId = "111",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        Product manualProduct = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-002",
            Name = "Manual product",
            Status = ProductStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        setupContext.Channels.AddRange(wbChannel, ozonChannel);
        setupContext.Products.AddRange(linkedProduct, manualProduct);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ProductRepository repository = new ProductRepository(readContext);

        // Act
        IReadOnlyList<Product> products = await repository.GetAllByStatusAsync(null, CancellationToken.None);

        // Assert
        products.Should().HaveCount(2);

        Product reloadedLinked = products.Single(p => p.Id == linkedProduct.Id);
        reloadedLinked.ChannelProducts.Should().ContainSingle();
        reloadedLinked.ChannelProducts.First().Channel.Should().NotBeNull();
        reloadedLinked.ChannelProducts.First().Channel.Name.Should().Be("Wildberries");

        Product reloadedManual = products.Single(p => p.Id == manualProduct.Id);
        reloadedManual.ChannelProducts.Should().BeEmpty();
    }

    private CatalogDbContext BuildContext(string databaseName)
    {
        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.TenantId).Returns(_tenantId);

        return new CatalogDbContext(options, tenantContextMock.Object);
    }
}
