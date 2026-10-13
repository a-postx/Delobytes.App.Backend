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
/// ComponentRepository resolves sortBy against a whitelist: name, supplier, validFrom,
/// createdAt. Sorting by supplier or validFrom reads the component's active price version
/// (<c>Prices.FirstOrDefault(p =&gt; p.IsActive)</c>); a component without an active price
/// always sorts last, in both directions.
/// </summary>
public class ComponentRepositoryOrderingTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task GetPagedAsync_SortByNameAscending_IsTheDefaultOrder()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Component beta = Component("Beta");
        Component alpha = Component("Alpha");

        setupContext.Components.AddRange(beta, alpha);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act: sortBy null and descending false reproduce the default "name asc" order.
        (int _, IReadOnlyList<Component> items) = await repository.GetPagedAsync(
            isActive: null,
            skip: null,
            take: null,
            sortBy: null,
            descending: false,
            CancellationToken.None);

        // Assert
        items.Select(c => c.Id).Should().ContainInOrder(alpha.Id, beta.Id);
    }

    [Fact]
    public async Task GetPagedAsync_UnknownSortKey_FallsBackToName()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Component beta = Component("Beta");
        Component alpha = Component("Alpha");

        setupContext.Components.AddRange(beta, alpha);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act
        (int _, IReadOnlyList<Component> items) = await repository.GetPagedAsync(
            isActive: null,
            skip: null,
            take: null,
            sortBy: "no-such-key",
            descending: false,
            CancellationToken.None);

        // Assert
        items.Select(c => c.Id).Should().ContainInOrder(alpha.Id, beta.Id);
    }

    [Fact]
    public async Task GetPagedAsync_SortByCreatedAtDescending_OrdersByCreationMoment()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Component oldest = Component("Oldest", createdAt: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Component newest = Component("Newest", createdAt: new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));

        setupContext.Components.AddRange(oldest, newest);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act
        (int _, IReadOnlyList<Component> items) = await repository.GetPagedAsync(
            isActive: null,
            skip: null,
            take: null,
            sortBy: "createdAt",
            descending: true,
            CancellationToken.None);

        // Assert
        items.Select(c => c.Id).Should().ContainInOrder(newest.Id, oldest.Id);
    }

    [Fact]
    public async Task GetPagedAsync_SortBySupplierAscending_ComponentsWithoutActivePriceSortLast()
    {
        // Arrange: a deleted component has no active price (its only price was deactivated).
        // The two EF providers disagree on where ORDER BY places nulls, so the repository pins
        // components without an active price to the end regardless of direction.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Supplier acme = new Supplier { Id = Guid.NewGuid(), Inn = "1111111111", Name = "Acme", IsActive = true };
        Supplier zenith = new Supplier { Id = Guid.NewGuid(), Inn = "2222222222", Name = "Zenith", IsActive = true };

        Component withAcme = ComponentWithActivePrice("Has Acme", acme);
        Component withZenith = ComponentWithActivePrice("Has Zenith", zenith);
        Component withoutActivePrice = ComponentWithInactivePrice("No active price", acme);

        setupContext.Suppliers.AddRange(acme, zenith);
        setupContext.Components.AddRange(withAcme, withZenith, withoutActivePrice);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act
        (int _, IReadOnlyList<Component> items) = await repository.GetPagedAsync(
            isActive: null,
            skip: null,
            take: null,
            sortBy: "supplier",
            descending: false,
            CancellationToken.None);

        // Assert
        items.Select(c => c.Id).Should().ContainInOrder(withAcme.Id, withZenith.Id, withoutActivePrice.Id);
    }

    [Fact]
    public async Task GetPagedAsync_SortBySupplierDescending_ComponentsWithoutActivePriceSortLast()
    {
        // Arrange: same requirement as ascending -- missing values stay last, not first.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Supplier acme = new Supplier { Id = Guid.NewGuid(), Inn = "1111111111", Name = "Acme", IsActive = true };
        Supplier zenith = new Supplier { Id = Guid.NewGuid(), Inn = "2222222222", Name = "Zenith", IsActive = true };

        Component withAcme = ComponentWithActivePrice("Has Acme", acme);
        Component withZenith = ComponentWithActivePrice("Has Zenith", zenith);
        Component withoutActivePrice = ComponentWithInactivePrice("No active price", acme);

        setupContext.Suppliers.AddRange(acme, zenith);
        setupContext.Components.AddRange(withAcme, withZenith, withoutActivePrice);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act
        (int _, IReadOnlyList<Component> items) = await repository.GetPagedAsync(
            isActive: null,
            skip: null,
            take: null,
            sortBy: "supplier",
            descending: true,
            CancellationToken.None);

        // Assert
        items.Select(c => c.Id).Should().ContainInOrder(withZenith.Id, withAcme.Id, withoutActivePrice.Id);
    }

    [Fact]
    public async Task GetPagedAsync_SortByValidFromAscending_ComponentsWithoutActivePriceSortLast()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Component earlier = ComponentWithActivePrice("Earlier", validFrom: new DateOnly(2024, 1, 1));
        Component later = ComponentWithActivePrice("Later", validFrom: new DateOnly(2024, 6, 1));
        Component withoutActivePrice = ComponentWithInactivePrice("No active price", null);

        setupContext.Components.AddRange(earlier, later, withoutActivePrice);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act
        (int _, IReadOnlyList<Component> items) = await repository.GetPagedAsync(
            isActive: null,
            skip: null,
            take: null,
            sortBy: "validFrom",
            descending: false,
            CancellationToken.None);

        // Assert
        items.Select(c => c.Id).Should().ContainInOrder(earlier.Id, later.Id, withoutActivePrice.Id);
    }

    [Fact]
    public async Task GetPagedAsync_SortByValidFromDescending_ComponentsWithoutActivePriceSortLast()
    {
        // Arrange
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Component earlier = ComponentWithActivePrice("Earlier", validFrom: new DateOnly(2024, 1, 1));
        Component later = ComponentWithActivePrice("Later", validFrom: new DateOnly(2024, 6, 1));
        Component withoutActivePrice = ComponentWithInactivePrice("No active price", null);

        setupContext.Components.AddRange(earlier, later, withoutActivePrice);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act: descending -- the latest ValidFrom comes first, missing comes last regardless.
        (int _, IReadOnlyList<Component> items) = await repository.GetPagedAsync(
            isActive: null,
            skip: null,
            take: null,
            sortBy: "validFrom",
            descending: true,
            CancellationToken.None);

        // Assert
        items.Select(c => c.Id).Should().ContainInOrder(later.Id, earlier.Id, withoutActivePrice.Id);
    }

    [Fact]
    public async Task GetPagedAsync_TiedSortValue_IsBrokenStablyById()
    {
        // Arrange: two components share the same name, so the Id tiebreaker must decide the
        // order deterministically -- otherwise paging across requests could reorder rows.
        string databaseName = Guid.NewGuid().ToString();
        CatalogDbContext setupContext = BuildContext(databaseName);

        Component first = Component("Same Name");
        Component second = Component("Same Name");
        bool firstIsLower = first.Id.CompareTo(second.Id) < 0;
        Guid lowerId = firstIsLower ? first.Id : second.Id;
        Guid higherId = firstIsLower ? second.Id : first.Id;

        setupContext.Components.AddRange(first, second);
        await setupContext.SaveChangesAsync();

        CatalogDbContext readContext = BuildContext(databaseName);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act
        (int _, IReadOnlyList<Component> items) = await repository.GetPagedAsync(
            isActive: null,
            skip: null,
            take: null,
            sortBy: "name",
            descending: false,
            CancellationToken.None);

        // Assert
        items.Select(c => c.Id).Should().ContainInOrder(lowerId, higherId);
    }

    private static Component Component(string name, DateTimeOffset? createdAt = null)
    {
        return new Component
        {
            Id = Guid.NewGuid(),
            Name = name,
            Unit = Unit.Piece,
            Category = ComponentCategory.Material,
            IsActive = true,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };
    }

    private static Component ComponentWithActivePrice(
        string name,
        Supplier? supplier = null,
        DateOnly? validFrom = null)
    {
        Component component = Component(name);
        component.Prices.Add(new ComponentPrice
        {
            Id = Guid.NewGuid(),
            ComponentId = component.Id,
            PricePerUnit = 1m,
            SupplierId = supplier?.Id,
            Supplier = supplier,
            ValidFrom = validFrom ?? DateOnly.FromDateTime(DateTime.UtcNow),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return component;
    }

    /// <summary>A deleted component: its only price version exists but is deactivated, so
    /// <c>Prices.FirstOrDefault(p =&gt; p.IsActive)</c> finds nothing.</summary>
    private static Component ComponentWithInactivePrice(string name, Supplier? supplier)
    {
        Component component = Component(name);
        component.IsActive = false;
        component.Prices.Add(new ComponentPrice
        {
            Id = Guid.NewGuid(),
            ComponentId = component.Id,
            PricePerUnit = 1m,
            SupplierId = supplier?.Id,
            Supplier = supplier,
            ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow),
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return component;
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
