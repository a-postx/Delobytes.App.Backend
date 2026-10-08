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
/// ProductRepository resolves sortBy against a whitelist. The list view now opens on the
/// "Изменено" column, so ordering by updatedat has to work for products that were never
/// edited: their UpdatedAt is null and the order must fall back to CreatedAt, matching
/// what the UI shows in that column.
/// </summary>
public class ProductRepositoryOrderingTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task GetPagedAsync_SortByUpdatedAtDescending_OrdersByLastModificationWithCreatedAtFallback()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        // Создан раньше всех и не менялся — по «Изменено» он самый старый.
        Product untouched = Product("SKU-untouched", "Untouched", new DateTimeOffset(2024, 1, 10, 12, 0, 0, TimeSpan.Zero));
        // Создан позже остальных, но ни разу не редактировался: в колонке «Изменено» — дата создания.
        Product lateUntouched = Product("SKU-late", "Late untouched", new DateTimeOffset(2024, 5, 20, 8, 0, 0, TimeSpan.Zero));
        // Правка старше создания второго товара — проверяет именно сортировку по UpdatedAt.
        Product edited = Product(
            "SKU-edited",
            "Edited",
            new DateTimeOffset(2024, 1, 5, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 6, 1, 10, 30, 0, TimeSpan.Zero));

        setupContext.Products.AddRange(untouched, lateUntouched, edited);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ProductRepository repository = new ProductRepository(readContext);

        // Act
        (int totalCount, IReadOnlyList<Product> items) = await repository.GetPagedAsync(
            null,
            null,
            null,
            "updatedAt",
            descending: true,
            CancellationToken.None);

        // Assert
        totalCount.Should().Be(3);
        items.Select(p => p.Id).Should().ContainInOrder(edited.Id, lateUntouched.Id, untouched.Id);
    }

    [Fact]
    public async Task GetPagedAsync_SortByUpdatedAtAscending_PutsNeverEditedProductsByCreationMoment()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Product untouched = Product("SKU-untouched", "Untouched", new DateTimeOffset(2024, 1, 10, 12, 0, 0, TimeSpan.Zero));
        Product lateUntouched = Product("SKU-late", "Late untouched", new DateTimeOffset(2024, 5, 20, 8, 0, 0, TimeSpan.Zero));
        Product edited = Product(
            "SKU-edited",
            "Edited",
            new DateTimeOffset(2024, 1, 5, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 6, 1, 10, 30, 0, TimeSpan.Zero));

        setupContext.Products.AddRange(untouched, lateUntouched, edited);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ProductRepository repository = new ProductRepository(readContext);

        // Act
        (int _, IReadOnlyList<Product> items) = await repository.GetPagedAsync(
            null,
            null,
            null,
            "updatedAt",
            descending: false,
            CancellationToken.None);

        // Assert
        items.Select(p => p.Id).Should().ContainInOrder(untouched.Id, lateUntouched.Id, edited.Id);
    }

    [Fact]
    public async Task GetPagedAsync_UnknownSortKey_FallsBackToName()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Product beta = Product("SKU-b", "Beta", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Product alpha = Product("SKU-a", "Alpha", new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero));

        setupContext.Products.AddRange(beta, alpha);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ProductRepository repository = new ProductRepository(readContext);

        // Act
        (int _, IReadOnlyList<Product> items) = await repository.GetPagedAsync(
            null,
            null,
            null,
            "no-such-key",
            descending: false,
            CancellationToken.None);

        // Assert
        items.Select(p => p.Id).Should().ContainInOrder(alpha.Id, beta.Id);
    }

    private static Product Product(string sku, string name, DateTimeOffset createdAt, DateTimeOffset? updatedAt = null)
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = sku,
            Name = name,
            Status = ProductStatus.Active,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
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
