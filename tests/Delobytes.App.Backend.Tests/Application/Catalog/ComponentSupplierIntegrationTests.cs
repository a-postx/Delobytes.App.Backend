using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.Components.UpdateComponent;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Components.GetComponent;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

public class ComponentSupplierIntegrationTests
{
    private readonly Mock<IComponentRepository> _componentRepoMock = new();
    private readonly Mock<IComponentPriceRepository> _priceRepoMock = new();
    private readonly Mock<ISupplierRepository> _supplierRepoMock = new();

    private static Supplier BuildSupplier(Guid? id = null, bool isActive = true)
    {
        return new Supplier
        {
            Id = id ?? Guid.NewGuid(),
            Inn = "7743013902",
            Name = "ООО Поставщик",
            Description = "Надежный поставщик",
            Phone = "+7 495 123-45-67",
            Email = "info@supplier.ru",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static Component BuildComponentWithPrice(Guid? componentId = null, Guid? supplierId = null)
    {
        Component component = new Component
        {
            Id = componentId ?? Guid.NewGuid(),
            Name = "Коробка 20x15x10",
            Description = "Упаковочная коробка",
            Unit = Unit.Piece,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        ComponentPrice price = new ComponentPrice
        {
            Id = Guid.NewGuid(),
            ComponentId = component.Id,
            PricePerUnit = 100.0m,
            SupplierId = supplierId,
            ValidFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        component.Prices.Add(price);

        return component;
    }

    // ── Component with Supplier Reference ──────────────────────────────────────────

    [Fact]
    public async Task UpdateComponent_PreservesSupplierReference()
    {
        // Arrange
        Guid supplierId = Guid.NewGuid();
        Component existing = BuildComponentWithPrice(supplierId: supplierId);
        ComponentPrice originalPrice = existing.Prices.First();

        _componentRepoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _componentRepoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdateComponentCommandHandler handler =
            new UpdateComponentCommandHandler(_componentRepoMock.Object);

        UpdateComponentCommand command = new UpdateComponentCommand
        {
            Id = existing.Id,
            Name = "Обновлённая коробка",
            Description = "Новое описание",
            Unit = Unit.Piece,
        };

        // Act
        UpdateComponentResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.Name.Should().Be("Обновлённая коробка");
        existing.Description.Should().Be("Новое описание");

        // Supplier reference in price must remain unchanged
        originalPrice.SupplierId.Should().Be(supplierId);
        originalPrice.PricePerUnit.Should().Be(100.0m);
    }

    [Fact]
    public async Task GetComponent_WithSupplier_ReturnsSupplierName()
    {
        // Arrange
        Guid supplierId = Guid.NewGuid();
        Component component = BuildComponentWithPrice(supplierId: supplierId);
        Supplier supplier = BuildSupplier(supplierId);

        ComponentPrice price = component.Prices.First();
        price.Supplier = supplier;

        _componentRepoMock
            .Setup(r => r.GetWithPricesByIdAsync(component.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(component);

        GetComponentQueryHandler handler =
            new GetComponentQueryHandler(_componentRepoMock.Object);

        // Act
        GetComponentResponse? response = await handler.Handle(
            new GetComponentQuery { Id = component.Id }, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response!.Id.Should().Be(component.Id);
        response.Name.Should().Be("Коробка 20x15x10");

        response.ActivePrice.Should().NotBeNull();
        response.ActivePrice!.SupplierId.Should().Be(supplierId);
        response.ActivePrice.SupplierName.Should().Be("ООО Поставщик");
    }

    [Fact]
    public async Task GetComponent_WithoutSupplier_ReturnsNullSupplier()
    {
        // Arrange
        Component component = BuildComponentWithPrice(supplierId: null);

        _componentRepoMock
            .Setup(r => r.GetWithPricesByIdAsync(component.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(component);

        GetComponentQueryHandler handler =
            new GetComponentQueryHandler(_componentRepoMock.Object);

        // Act
        GetComponentResponse? response = await handler.Handle(
            new GetComponentQuery { Id = component.Id }, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();

        response.ActivePrice.Should().NotBeNull();
        response.ActivePrice!.SupplierId.Should().BeNull();
        response.ActivePrice.SupplierName.Should().BeNull();
    }

    [Fact]
    public async Task Component_SupplierDeactivation_DoesNotAffectExistingPrices()
    {
        // Arrange
        Guid supplierId = Guid.NewGuid();
        Component component = BuildComponentWithPrice(supplierId: supplierId);
        ComponentPrice price = component.Prices.First();

        Supplier supplier = BuildSupplier(supplierId, isActive: false);
        price.Supplier = supplier;

        // Assert
        price.SupplierId.Should().Be(supplierId);
        price.Supplier.IsActive.Should().BeFalse();
        component.Prices.Should().Contain(price);
    }

    [Fact]
    public async Task Component_MultipleSupplierChanges_CreatesHistoricalPriceVersions()
    {
        // Arrange
        Guid supplier1Id = Guid.NewGuid();
        Guid supplier2Id = Guid.NewGuid();
        Guid componentId = Guid.NewGuid();

        Component component = new Component
        {
            Id = componentId,
            Name = "Пузырчатая плёнка",
            Unit = Unit.Meter,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        ComponentPrice price1 = new ComponentPrice
        {
            Id = Guid.NewGuid(),
            ComponentId = componentId,
            PricePerUnit = 50.0m,
            SupplierId = supplier1Id,
            ValidFrom = new DateOnly(2025, 1, 1),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMonths(-6),
        };

        ComponentPrice price2 = new ComponentPrice
        {
            Id = Guid.NewGuid(),
            ComponentId = componentId,
            PricePerUnit = 55.0m,
            SupplierId = supplier2Id,
            ValidFrom = new DateOnly(2025, 6, 1),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMonths(-3),
        };

        ComponentPrice price3 = new ComponentPrice
        {
            Id = Guid.NewGuid(),
            ComponentId = componentId,
            PricePerUnit = 60.0m,
            SupplierId = supplier1Id,
            ValidFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        component.Prices.Add(price1);
        component.Prices.Add(price2);
        component.Prices.Add(price3);

        // Assert
        component.Prices.Should().HaveCount(3);
        component.Prices.Should().Contain(p => p.SupplierId == supplier1Id && p.ValidFrom == new DateOnly(2025, 1, 1));
        component.Prices.Should().Contain(p => p.SupplierId == supplier2Id && p.ValidFrom == new DateOnly(2025, 6, 1));
        component.Prices.Should().Contain(p => p.SupplierId == supplier1Id && p.ValidFrom == new DateOnly(2026, 1, 1));

        ComponentPrice? oldestPrice = component.Prices.OrderBy(p => p.ValidFrom).First();
        oldestPrice.SupplierId.Should().Be(supplier1Id);
        oldestPrice.PricePerUnit.Should().Be(50.0m);

        ComponentPrice? newestPrice = component.Prices.OrderByDescending(p => p.ValidFrom).First();
        newestPrice.SupplierId.Should().Be(supplier1Id);
        newestPrice.PricePerUnit.Should().Be(60.0m);
    }

    [Fact]
    public void ComponentPrice_SupplierNullable_AllowsNoSupplier()
    {
        // Arrange
        Guid componentId = Guid.NewGuid();

        ComponentPrice priceWithoutSupplier = new ComponentPrice
        {
            Id = Guid.NewGuid(),
            ComponentId = componentId,
            PricePerUnit = 75.0m,
            SupplierId = null,
            ValidFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // Assert
        priceWithoutSupplier.SupplierId.Should().BeNull();
        priceWithoutSupplier.Supplier.Should().BeNull();
        priceWithoutSupplier.PricePerUnit.Should().Be(75.0m);
    }
}
