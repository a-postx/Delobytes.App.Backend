using System;
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
/// ComponentRepository.GetStatusCountsAsync must describe the same filtered set the list
/// query is showing: the search term narrows the counters, and an omitted term counts the
/// whole catalog.
/// </summary>
public class ComponentRepositoryCountsTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task GetStatusCountsAsync_WithSearch_CountsOnlyMatchingComponents()
    {
        // Arrange: two active matches, one inactive match and three non-matching components
        // that must not appear in any counter.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Component inactiveMatch = Component("Bolt Brass");
        inactiveMatch.IsActive = false;

        setupContext.Components.AddRange(
            Component("Bolt Steel"),
            Component("Bolt Chrome"),
            inactiveMatch,
            Component("Nut Steel"),
            Component("Washer Steel"),
            Component("Screw Steel"));
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act
        (int active, int inactive, int all) = await repository.GetStatusCountsAsync(
            CancellationToken.None,
            "bolt");

        // Assert
        active.Should().Be(2);
        inactive.Should().Be(1);
        all.Should().Be(3);
    }

    [Fact]
    public async Task GetStatusCountsAsync_WithoutSearch_CountsTheWholeCatalog()
    {
        // Arrange: the unfiltered counters must not regress when the search argument is omitted.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Component inactive = Component("Hammer");
        inactive.IsActive = false;

        setupContext.Components.AddRange(
            Component("Bolt Steel"),
            Component("Nut Steel"),
            inactive);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act
        (int active, int inactiveCount, int all) = await repository.GetStatusCountsAsync(CancellationToken.None);

        // Assert
        active.Should().Be(2);
        inactiveCount.Should().Be(1);
        all.Should().Be(3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GetStatusCountsAsync_BlankSearch_CountsTheWholeCatalog(string? search)
    {
        // Arrange: blank search must behave exactly like a missing parameter.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        setupContext.Components.AddRange(Component("Bolt Steel"), Component("Nut Steel"));
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act
        (int active, int inactive, int all) = await repository.GetStatusCountsAsync(
            CancellationToken.None,
            search);

        // Assert
        active.Should().Be(2);
        inactive.Should().Be(0);
        all.Should().Be(2);
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
