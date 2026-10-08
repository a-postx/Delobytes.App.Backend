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
/// ProductRepository free-text search over Product.Name and Product.Sku.
/// The contract: case-insensitive substring, "name OR sku", a blank term means "no filter",
/// the term is trimmed and capped at the name column length, and the search narrows the set
/// before paging and before the status counters are computed.
/// </summary>
public class ProductRepositorySearchTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task GetPagedAsync_SearchMatchesNameFragmentInDifferentCase()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Product widget = Product("SKU-1", "Blue Widget", ProductStatus.Active);
        Product gadget = Product("SKU-2", "Red Gadget", ProductStatus.Active);

        setupContext.Products.AddRange(widget, gadget);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ProductRepository repository = new ProductRepository(readContext);

        // Act: the stored name is "Blue Widget", the term arrives in lower case.
        (int totalCount, IReadOnlyList<Product> items) = await repository.GetPagedAsync(
            null,
            null,
            null,
            null,
            descending: false,
            CancellationToken.None,
            "widget");

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle().Which.Id.Should().Be(widget.Id);
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesSkuFragmentInDifferentCase()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Product matching = Product("ART-00123", "Screwdriver", ProductStatus.Active);
        Product other = Product("BLD-77777", "Hammer", ProductStatus.Active);

        setupContext.Products.AddRange(matching, other);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ProductRepository repository = new ProductRepository(readContext);

        // Act: the stored SKU is upper case, the term is lower case.
        (int totalCount, IReadOnlyList<Product> items) = await repository.GetPagedAsync(
            null,
            null,
            null,
            null,
            descending: false,
            CancellationToken.None,
            "art-001");

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle().Which.Id.Should().Be(matching.Id);
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesWhenOnlySkuContainsTheTerm()
    {
        // Arrange: the name has nothing in common with the term, only the SKU does --
        // this is the "Name OR Sku" half of the contract.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Product bySkuOnly = Product("UNIQ-954", "Plain item", ProductStatus.Active);
        Product unrelated = Product("ZZZ-000", "Plain item", ProductStatus.Active);

        setupContext.Products.AddRange(bySkuOnly, unrelated);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ProductRepository repository = new ProductRepository(readContext);

        // Act
        (int totalCount, IReadOnlyList<Product> items) = await repository.GetPagedAsync(
            null,
            null,
            null,
            null,
            descending: false,
            CancellationToken.None,
            "uniq-954");

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle().Which.Id.Should().Be(bySkuOnly.Id);
    }

    [Fact]
    public async Task GetPagedAsync_SearchWithSurroundingWhitespace_MatchesTrimmedTerm()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Product widget = Product("SKU-1", "Blue Widget", ProductStatus.Active);
        Product gadget = Product("SKU-2", "Red Gadget", ProductStatus.Active);

        setupContext.Products.AddRange(widget, gadget);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ProductRepository repository = new ProductRepository(readContext);

        // Act: user typed trailing/leading spaces around the term.
        (int totalCount, IReadOnlyList<Product> items) = await repository.GetPagedAsync(
            null,
            null,
            null,
            null,
            descending: false,
            CancellationToken.None,
            "  widget  ");

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle().Which.Id.Should().Be(widget.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData(null)]
    public async Task GetPagedAsync_BlankSearch_DoesNotFilter(string? search)
    {
        // Arrange: an empty or whitespace-only term must behave exactly like a missing parameter.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        setupContext.Products.AddRange(
            Product("SKU-1", "Blue Widget", ProductStatus.Active),
            Product("SKU-2", "Red Gadget", ProductStatus.Archived));
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ProductRepository repository = new ProductRepository(readContext);

        // Act
        (int totalCount, IReadOnlyList<Product> items) = await repository.GetPagedAsync(
            null,
            null,
            null,
            null,
            descending: false,
            CancellationToken.None,
            search);

        // Assert
        totalCount.Should().Be(2);
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPagedAsync_SearchTermLongerThanNameColumn_IsTruncatedTo200Characters()
    {
        // Arrange: a term capped at the name column length (200) matches a 200-character name;
        // the same term with extra trailing characters must behave identically, which only holds
        // if the extra characters are dropped before the comparison.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        string longName = new string('a', 200);
        Product product = Product("SKU-1", longName, ProductStatus.Active);
        setupContext.Products.Add(product);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ProductRepository repository = new ProductRepository(readContext);

        string overlongSearch = new string('a', 200) + "ZZZ";

        // Act
        (int totalCount, IReadOnlyList<Product> _) = await repository.GetPagedAsync(
            null,
            null,
            null,
            null,
            descending: false,
            CancellationToken.None,
            overlongSearch);

        // Assert
        totalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetPagedAsync_NoMatch_ReturnsEmptyResultAndZeroTotalCount()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        setupContext.Products.AddRange(
            Product("SKU-1", "Blue Widget", ProductStatus.Active),
            Product("SKU-2", "Red Gadget", ProductStatus.Active));
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ProductRepository repository = new ProductRepository(readContext);

        // Act
        (int totalCount, IReadOnlyList<Product> items) = await repository.GetPagedAsync(
            null,
            null,
            null,
            null,
            descending: false,
            CancellationToken.None,
            "no-such-product-anywhere");

        // Assert
        totalCount.Should().Be(0);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPagedAsync_SearchAndStatus_AreCombinedWithAnd()
    {
        // Arrange: the term matches three products, but only one of them is Archived --
        // status must keep filtering the search results, not replace the filter.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Product activeMatch = Product("SKU-1", "Blue Widget", ProductStatus.Active);
        Product archivedMatch = Product("SKU-2", "Red Widget", ProductStatus.Archived);
        Product otherName = Product("SKU-3", "Blue Widget", ProductStatus.Archived);
        Product noMatch = Product("SKU-4", "Hammer", ProductStatus.Archived);

        setupContext.Products.AddRange(activeMatch, archivedMatch, otherName, noMatch);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ProductRepository repository = new ProductRepository(readContext);

        // Act
        (int totalCount, IReadOnlyList<Product> items) = await repository.GetPagedAsync(
            ProductStatus.Archived,
            null,
            null,
            null,
            descending: false,
            CancellationToken.None,
            "widget");

        // Assert
        totalCount.Should().Be(2);
        items.Select(p => p.Id).Should().BeEquivalentTo(new[] { archivedMatch.Id, otherName.Id });
        items.Should().OnlyContain(p => p.Status == ProductStatus.Archived);
    }

    [Fact]
    public async Task GetPagedAsync_SearchWithPaging_CountsFilteredRowsAndPagesTheFilteredSet()
    {
        // Arrange: five rows match the search out of eight; paging must be applied after the
        // filter, so TotalCount reports 5 and page 2 of size 2 holds rows 3 and 4 of the match.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        List<Product> matching = new List<Product>();
        for (int i = 0; i < 5; i++)
        {
            // Name sorted alphabetically: Match 0 .. Match 4, so the expected slice is deterministic.
            matching.Add(Product($"SKU-M{i}", $"Match {i}", ProductStatus.Active));
        }

        setupContext.Products.AddRange(matching);
        setupContext.Products.AddRange(
            Product("SKU-X1", "Other one", ProductStatus.Active),
            Product("SKU-X2", "Other two", ProductStatus.Active),
            Product("SKU-X3", "Other three", ProductStatus.Active));
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ProductRepository repository = new ProductRepository(readContext);

        // Act: skip 2, take 2 out of the filtered set.
        (int totalCount, IReadOnlyList<Product> items) = await repository.GetPagedAsync(
            null,
            skip: 2,
            take: 2,
            sortBy: "name",
            descending: false,
            CancellationToken.None,
            "match");

        // Assert
        totalCount.Should().Be(5);
        items.Should().HaveCount(2);
        items.Select(p => p.Name).Should().ContainInOrder("Match 2", "Match 3");
    }

    [Fact]
    public async Task GetStatusCountsAsync_WithSearch_CountsOnlyMatchingProducts()
    {
        // Arrange: two active matches, one archived match and three non-matching products
        // that must not appear in any counter.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        setupContext.Products.AddRange(
            Product("SKU-1", "Blue Widget", ProductStatus.Active),
            Product("SKU-2", "Red Widget", ProductStatus.Active),
            Product("SKU-3", "Green Widget", ProductStatus.Archived),
            Product("SKU-4", "Hammer", ProductStatus.Active),
            Product("SKU-5", "Saw", ProductStatus.Active),
            Product("SKU-6", "Drill", ProductStatus.Archived));
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ProductRepository repository = new ProductRepository(readContext);

        // Act
        (int active, int archived, int all) = await repository.GetStatusCountsAsync(
            CancellationToken.None,
            "widget");

        // Assert
        active.Should().Be(2);
        archived.Should().Be(1);
        all.Should().Be(3);
    }

    [Fact]
    public async Task GetStatusCountsAsync_WithoutSearch_CountsTheWholeCatalog()
    {
        // Arrange: the unfiltered counters must not regress when the search argument is omitted.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        setupContext.Products.AddRange(
            Product("SKU-1", "Blue Widget", ProductStatus.Active),
            Product("SKU-2", "Red Widget", ProductStatus.Active),
            Product("SKU-3", "Hammer", ProductStatus.Archived));
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ProductRepository repository = new ProductRepository(readContext);

        // Act
        (int active, int archived, int all) = await repository.GetStatusCountsAsync(CancellationToken.None);

        // Assert
        active.Should().Be(2);
        archived.Should().Be(1);
        all.Should().Be(3);
    }

    private static Product Product(string sku, string name, ProductStatus status)
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = sku,
            Name = name,
            Status = status,
            CreatedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
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
