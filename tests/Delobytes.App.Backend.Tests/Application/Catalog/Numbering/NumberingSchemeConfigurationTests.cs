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

namespace Delobytes.App.Backend.Tests.Application.Catalog.Numbering;

/// <summary>
/// Uniqueness is asserted through model metadata, not by inserting duplicates: the InMemory
/// provider does not enforce unique indexes and would let a duplicate insert pass regardless.
/// </summary>
public class NumberingSchemeConfigurationTests
{
    private static CatalogDbContext BuildContext()
    {
        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("numbering-configuration-" + Guid.NewGuid())
            .Options;

        Mock<ITenantContext> tenantMock = new Mock<ITenantContext>();
        tenantMock.Setup(t => t.TenantId).Returns(Guid.NewGuid());

        return new CatalogDbContext(options, tenantMock.Object);
    }

    [Fact]
    public void NumberingSchemeModel_CodeIndex_IsUniquePerTenant()
    {
        using CatalogDbContext context = BuildContext();

        IEntityType schemeType = context.Model.FindEntityType(typeof(NumberingScheme))!;
        IIndex index = schemeType.GetIndexes()
            .Single(candidate => candidate.GetDatabaseName() == "IX_NumberingSchemes_TenantId_Code");

        index.IsUnique.Should().BeTrue();
        index.Properties.Select(property => property.Name)
            .Should().Equal("TenantId", nameof(NumberingScheme.Code));
        index.GetFilter().Should().BeNull();
    }

    [Fact]
    public void NumberingSegmentModel_SchemeOrderIndex_IsUnique()
    {
        using CatalogDbContext context = BuildContext();

        IEntityType segmentType = context.Model.FindEntityType(typeof(NumberingSegment))!;
        IIndex index = segmentType.GetIndexes()
            .Single(candidate => candidate.GetDatabaseName() == "IX_NumberingSegments_SchemeId_Order");

        index.IsUnique.Should().BeTrue();
        index.Properties.Select(property => property.Name)
            .Should().Equal(nameof(NumberingSegment.SchemeId), nameof(NumberingSegment.Order));
    }
}
