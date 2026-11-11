using System;
using System.Linq;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Persistence.Converters;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Persistence;

public class SharedModelConventionsTests
{
    private static IModel BuildModel()
    {
        Mock<ITenantContext> tenant = TenantContextMocks.WithTenant(Guid.NewGuid());
        DbContextOptions<TestPersistenceDbContext> options = PersistenceTestOptions.Build<TestPersistenceDbContext>(
            PersistenceTestOptions.NewDatabaseName("model"));

        using TestPersistenceDbContext context = new TestPersistenceDbContext(options, tenant.Object);
        return context.Model;
    }

    private static IProperty FindProperty(IModel model, Type entityType, string propertyName)
    {
        IProperty? property = model.FindEntityType(entityType)!.FindProperty(propertyName);
        property.Should().NotBeNull();
        return property!;
    }

    [Fact]
    public void Model_AddsRequiredGuidShadowTenantId()
    {
        // Act
        IProperty tenantId = FindProperty(BuildModel(), typeof(Widget), "TenantId");

        // Assert
        tenantId.ClrType.Should().Be(typeof(Guid));
        tenantId.IsNullable.Should().BeFalse();
        tenantId.PropertyInfo.Should().BeNull();
    }

    [Fact]
    public void Model_AddsSingleColumnTenantIdIndex()
    {
        // Act
        IEntityType widget = BuildModel().FindEntityType(typeof(Widget))!;

        // Assert
        widget.GetIndexes().Should()
            .ContainSingle(i => i.Properties.Count == 1 && i.Properties[0].Name == "TenantId");
    }

    [Fact]
    public void Model_ReusesRealTenantIdProperty()
    {
        // Act
        IEntityType badge = BuildModel().FindEntityType(typeof(Badge))!;
        IProperty tenantId = badge.FindProperty("TenantId")!;

        // Assert
        tenantId.PropertyInfo.Should().NotBeNull();
        tenantId.PropertyInfo!.Name.Should().Be("TenantId");
        badge.GetIndexes().Count(i => i.Properties.Count == 1 && i.Properties[0].Name == "TenantId")
            .Should().Be(1);
    }

    [Fact]
    public void Model_DoesNotAddTenantIdToNonScopedEntity()
    {
        // Act
        IEntityType note = BuildModel().FindEntityType(typeof(PlainNote))!;

        // Assert
        note.FindProperty("TenantId").Should().BeNull();
        note.GetQueryFilter().Should().BeNull();
    }

    [Fact]
    public void Model_MapsRowVersionToXminXidConcurrencyToken()
    {
        // Act
        IProperty rowVersion = FindProperty(BuildModel(), typeof(VersionedWidget), nameof(VersionedWidget.RowVersion));

        // Assert
        rowVersion.ClrType.Should().Be(typeof(uint));
        rowVersion.IsNullable.Should().BeFalse();
        rowVersion.IsConcurrencyToken.Should().BeTrue();
        rowVersion.ValueGenerated.Should().Be(ValueGenerated.OnAddOrUpdate);
        rowVersion.GetColumnName().Should().Be("xmin");
        rowVersion.FindAnnotation(RelationalAnnotationNames.ColumnType)!.Value.Should().Be("xid");
    }

    [Fact]
    public void Model_AppliesUtcConvertersToDateTimeOffsetProperties()
    {
        // Act
        IModel model = BuildModel();
        IProperty createdAt = FindProperty(model, typeof(Widget), nameof(Widget.CreatedAt));
        IProperty updatedAt = FindProperty(model, typeof(Widget), nameof(Widget.UpdatedAt));

        // Assert
        createdAt.GetValueConverter().Should().BeOfType<DateTimeOffsetToUtcConverter>();
        updatedAt.GetValueConverter().Should().BeOfType<NullableDateTimeOffsetToUtcConverter>();
    }

    [Fact]
    public void Model_AttachesQueryFilterToTenantScopedEntities()
    {
        // Act
        IModel model = BuildModel();

        // Assert
        model.FindEntityType(typeof(Widget))!.GetQueryFilter().Should().NotBeNull();
        model.FindEntityType(typeof(Badge))!.GetQueryFilter().Should().NotBeNull();
        model.FindEntityType(typeof(VersionedWidget))!.GetQueryFilter().Should().NotBeNull();
    }
}
