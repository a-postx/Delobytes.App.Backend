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
/// ComponentRepository free-text search over Component.Name only (unlike products, the
/// description is not searched). The contract: case-insensitive substring, a blank term means
/// "no filter", the term is trimmed and capped at the name column length, and the search
/// narrows the set before paging.
/// </summary>
public class ComponentRepositorySearchTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task GetPagedAsync_SearchMatchesNameFragmentInDifferentCase()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Component bolt = Component("Болт M6");
        Component nut = Component("Гайка M6");

        setupContext.Components.AddRange(bolt, nut);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act: the stored name is "Болт M6", the term arrives in lower case.
        (int totalCount, IReadOnlyList<Component> items) = await repository.GetPagedAsync(
            null,
            null,
            null,
            descending: false,
            CancellationToken.None,
            "болт");

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle().Which.Id.Should().Be(bolt.Id);
    }

    [Fact]
    public async Task GetPagedAsync_SearchDoesNotMatchDescription()
    {
        // Arrange: unlike products, the description is never searched for components.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Component component = Component("Болт M6");
        component.Description = "unique-description-term";

        setupContext.Components.Add(component);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act
        (int totalCount, IReadOnlyList<Component> items) = await repository.GetPagedAsync(
            null,
            null,
            null,
            descending: false,
            CancellationToken.None,
            "unique-description-term");

        // Assert
        totalCount.Should().Be(0);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPagedAsync_SearchWithSurroundingWhitespace_MatchesTrimmedTerm()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Component bolt = Component("Болт M6");
        Component nut = Component("Гайка M6");

        setupContext.Components.AddRange(bolt, nut);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act: user typed trailing/leading spaces around the term.
        (int totalCount, IReadOnlyList<Component> items) = await repository.GetPagedAsync(
            null,
            null,
            null,
            descending: false,
            CancellationToken.None,
            "  болт  ");

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle().Which.Id.Should().Be(bolt.Id);
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

        setupContext.Components.AddRange(Component("Болт M6"), Component("Гайка M6"));
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act
        (int totalCount, IReadOnlyList<Component> items) = await repository.GetPagedAsync(
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
        // Arrange: a term capped at 200 characters matches a 200-character name; the same term
        // with extra trailing characters must behave identically, which only holds if the extra
        // characters are dropped before the comparison.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        string longName = new string('a', 200);
        Component component = Component(longName);
        setupContext.Components.Add(component);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ComponentRepository repository = new ComponentRepository(readContext);

        string overlongSearch = new string('a', 200) + "ZZZ";

        // Act
        (int totalCount, IReadOnlyList<Component> _) = await repository.GetPagedAsync(
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

        setupContext.Components.AddRange(Component("Болт M6"), Component("Гайка M6"));
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act
        (int totalCount, IReadOnlyList<Component> items) = await repository.GetPagedAsync(
            null,
            null,
            null,
            descending: false,
            CancellationToken.None,
            "no-such-component-anywhere");

        // Assert
        totalCount.Should().Be(0);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPagedAsync_SearchWithPaging_CountsFilteredRowsAndPagesTheFilteredSet()
    {
        // Arrange: five rows match the search out of eight; paging must be applied after the
        // filter, so TotalCount reports 5 and page 2 of size 2 holds rows 3 and 4 of the match.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        List<Component> matching = new List<Component>();
        for (int i = 0; i < 5; i++)
        {
            matching.Add(Component($"Match {i}"));
        }

        setupContext.Components.AddRange(matching);
        setupContext.Components.AddRange(
            Component("Other one"),
            Component("Other two"),
            Component("Other three"));
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act: skip 2, take 2 out of the filtered set.
        (int totalCount, IReadOnlyList<Component> items) = await repository.GetPagedAsync(
            skip: 2,
            take: 2,
            sortBy: "name",
            descending: false,
            CancellationToken.None,
            "match");

        // Assert
        totalCount.Should().Be(5);
        items.Should().HaveCount(2);
        items.Select(c => c.Name).Should().ContainInOrder("Match 2", "Match 3");
    }

    private static Component Component(string name)
    {
        return new Component
        {
            Id = Guid.NewGuid(),
            Name = name,
            Unit = Unit.Piece,
            Category = ComponentCategory.Material,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
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
