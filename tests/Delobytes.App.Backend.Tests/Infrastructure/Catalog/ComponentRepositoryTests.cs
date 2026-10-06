using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;
using Delobytes.App.Backend.Contracts.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Delobytes.App.Backend.Tests.Infrastructure.Catalog;

public class ComponentRepositoryTests
{
    [Fact]
    public async Task GetByIdsAsync_EmptyInput_ReturnsEmptyListWithoutLoadingAnything()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        DbContextOptions<CatalogDbContext> options = BuildOptions();

        // Seed and read through separate contexts: saving leaves the entity tracked, so a shared context
        // would keep a Component in its tracker no matter what the repository does, and the assertion
        // below would hold even if the early return were removed.
        using (CatalogDbContext seedContext = BuildContext(options, tenantId))
        {
            seedContext.Components.Add(BuildComponent("Ткань", ComponentCategory.Material));
            await seedContext.SaveChangesAsync();
        }

        using CatalogDbContext readContext = BuildContext(options, tenantId);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act
        IReadOnlyList<Component> result = await repository.GetByIdsAsync(
            new List<Guid>(),
            CancellationToken.None);

        // Assert. The empty input is answered from the early return, so nothing is materialized:
        // a query would still match no rows here, but it would have gone to the database to find that out.
        result.Should().BeEmpty();
        readContext.ChangeTracker.Entries<Component>().Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdsAsync_ReturnsEveryRequestedComponent()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        Guid otherId = Guid.NewGuid();
        DbContextOptions<CatalogDbContext> options = BuildOptions();

        using (CatalogDbContext context = BuildContext(options, tenantId))
        {
            context.Components.AddRange(
                BuildComponent("Ткань", ComponentCategory.Material, firstId),
                BuildComponent("Доставка", ComponentCategory.Logistics, secondId),
                BuildComponent("Коробка", ComponentCategory.Packaging, otherId));
            await context.SaveChangesAsync();
        }

        using CatalogDbContext readContext = BuildContext(options, tenantId);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act
        IReadOnlyList<Component> result = await repository.GetByIdsAsync(
            new List<Guid> { firstId, secondId },
            CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Select(component => component.Id).Should().BeEquivalentTo(new[] { firstId, secondId });
        result.Should().OnlyContain(component => component.Id != otherId);
    }

    [Fact]
    public async Task GetByIdsAsync_UnknownId_ReturnsOnlyTheKnownOnes()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid knownId = Guid.NewGuid();
        Guid unknownId = Guid.NewGuid();
        DbContextOptions<CatalogDbContext> options = BuildOptions();

        using (CatalogDbContext context = BuildContext(options, tenantId))
        {
            context.Components.Add(BuildComponent("Ткань", ComponentCategory.Material, knownId));
            await context.SaveChangesAsync();
        }

        using CatalogDbContext readContext = BuildContext(options, tenantId);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act
        IReadOnlyList<Component> result = await repository.GetByIdsAsync(
            new List<Guid> { knownId, unknownId },
            CancellationToken.None);

        // Assert. The caller decides what a missing component means; the repository only reports what exists.
        Component component = result.Should().ContainSingle().Subject;
        component.Id.Should().Be(knownId);
    }

    [Fact]
    public async Task GetByIdsAsync_RepeatedIds_ReturnsEachComponentOnce()
    {
        // Arrange
        Guid tenantId = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();
        DbContextOptions<CatalogDbContext> options = BuildOptions();

        using (CatalogDbContext context = BuildContext(options, tenantId))
        {
            context.Components.Add(BuildComponent("Ткань", ComponentCategory.Material, componentId));
            await context.SaveChangesAsync();
        }

        using CatalogDbContext readContext = BuildContext(options, tenantId);
        ComponentRepository repository = new ComponentRepository(readContext);

        // Act
        IReadOnlyList<Component> result = await repository.GetByIdsAsync(
            new List<Guid> { componentId, componentId },
            CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
    }

    private static DbContextOptions<CatalogDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("component-repository-tests-" + Guid.NewGuid())
            .Options;
    }

    private static CatalogDbContext BuildContext(
        DbContextOptions<CatalogDbContext> options,
        Guid tenantId)
    {
        Mock<ITenantContext> tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(context => context.TenantId).Returns(tenantId);
        return new CatalogDbContext(options, tenantContextMock.Object);
    }

    private static Component BuildComponent(
        string name,
        ComponentCategory category,
        Guid? id = null)
    {
        return new Component
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Unit = Unit.Piece,
            Category = category,
            IsActive = true,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
    }
}
