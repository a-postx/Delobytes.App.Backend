using System;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Contracts.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Catalog;

/// <summary>
/// A failed card must not keep the number it allocated: EF leaves the advanced counter in the
/// change tracker even after the savepoint rolls the row back, so the card scope reloads every
/// sequence it did not create. EF InMemory has no savepoints, so this covers the change-tracker
/// half only; the PostgreSQL savepoint path itself is not proven here.
/// </summary>
public class ProductImportStoreNumberingRollbackTests
{
    private static DbContextOptions<CatalogDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("product-import-numbering-rollback-" + Guid.NewGuid())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private static Mock<ITenantContext> TenantMock(Guid tenantId)
    {
        Mock<ITenantContext> mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        return mock;
    }

    private static NumberingSequence BuildSequence(string name, int nextNumber)
    {
        return new NumberingSequence
        {
            Id = Guid.NewGuid(),
            Name = name,
            StartNumber = 1,
            Step = 1,
            NextNumber = nextNumber,
            IsActive = true,
        };
    }

    [Fact]
    public async Task RollbackAsync_FailedCard_ReloadsCommittedCounterAndDetachesNewSequence()
    {
        Guid tenantId = Guid.NewGuid();
        DbContextOptions<CatalogDbContext> options = BuildOptions();

        NumberingSequence persisted = BuildSequence("Product code", 1);

        using (CatalogDbContext seed = new CatalogDbContext(options, TenantMock(tenantId).Object))
        {
            seed.NumberingSequences.Add(persisted);
            await seed.SaveChangesAsync();
        }

        using CatalogDbContext context = new CatalogDbContext(options, TenantMock(tenantId).Object);

        ProductImportStore store = new ProductImportStore(context);
        NumberingSequence tracked = await context.NumberingSequences.SingleAsync(s => s.Id == persisted.Id);

        using IProductImportBatchTransaction batch = await store.BeginBatchAsync(default);
        using IProductImportCardScope card = await batch.BeginCardAsync(default);

        tracked.NextNumber = 42;

        NumberingSequence createdInCard = BuildSequence("Created in card", 1);
        context.NumberingSequences.Add(createdInCard);

        await card.RollbackAsync(default);

        tracked.NextNumber.Should().Be(1);
        context.Entry(tracked).State.Should().Be(EntityState.Unchanged);
        context.Entry(createdInCard).State.Should().Be(EntityState.Detached);
    }
}
