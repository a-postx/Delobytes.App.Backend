using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.Suppliers.CreateSupplier;
using Delobytes.App.Backend.Catalog.Application.Commands.Suppliers.DeleteSupplier;
using Delobytes.App.Backend.Catalog.Application.Commands.Suppliers.UpdateSupplier;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Suppliers.GetSupplier;
using Delobytes.App.Backend.Catalog.Application.Queries.Suppliers.GetSuppliers;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

public class SupplierCommandHandlerTests
{
    private readonly Mock<ISupplierRepository> _repoMock = new();

    private static Supplier BuildSupplier(Guid? id = null, bool isActive = true)
        => new Supplier
        {
            Id = id ?? Guid.NewGuid(),
            Inn = "7743013902",
            Name = "ООО Поставщик",
            Description = "Надежный поставщик упаковочных материалов",
            Phone = "+7 495 123-45-67",
            Email = "info@supplier.ru",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // ── Create ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSupplier_ValidCommand_AddsEntityAndReturnsId()
    {
        // Arrange
        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateSupplierCommandHandler handler =
            new CreateSupplierCommandHandler(_repoMock.Object);

        CreateSupplierCommand command = new CreateSupplierCommand
        {
            Inn = "7743013902",
            Name = "ООО Поставщик",
            Description = "Основной поставщик компонентов",
            Phone = "+7 495 123-45-67",
            Email = "contact@supplier.ru",
        };

        // Act
        CreateSupplierResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Id.Should().NotBe(Guid.Empty);

        _repoMock.Verify(
            r => r.Add(It.Is<Supplier>(s =>
                s.Inn == "7743013902" &&
                s.Name == "ООО Поставщик" &&
                s.Description == "Основной поставщик компонентов" &&
                s.Phone == "+7 495 123-45-67" &&
                s.Email == "contact@supplier.ru" &&
                s.IsActive == true)),
            Times.Once);

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateSupplier_NullableFieldsPreserved()
    {
        // Arrange
        Supplier? capturedSupplier = null;

        _repoMock
            .Setup(r => r.Add(It.IsAny<Supplier>()))
            .Callback<Supplier>(s => capturedSupplier = s);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateSupplierCommandHandler handler =
            new CreateSupplierCommandHandler(_repoMock.Object);

        CreateSupplierCommand command = new CreateSupplierCommand
        {
            Inn = "7743013902",
            Name = "ООО Минималист",
            Description = null,
            Phone = null,
            Email = null,
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        capturedSupplier.Should().NotBeNull();
        capturedSupplier!.Inn.Should().Be("7743013902");
        capturedSupplier.Name.Should().Be("ООО Минималист");
        capturedSupplier.Description.Should().BeNull();
        capturedSupplier.Phone.Should().BeNull();
        capturedSupplier.Email.Should().BeNull();
    }

    [Fact]
    public async Task CreateSupplier_IndividualInn_12Digits()
    {
        // Arrange
        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateSupplierCommandHandler handler =
            new CreateSupplierCommandHandler(_repoMock.Object);

        CreateSupplierCommand command = new CreateSupplierCommand
        {
            Inn = "771234567890",
            Name = "ИП Иванов",
            Description = "Индивидуальный предприниматель",
            Phone = "+7 916 123-45-67",
            Email = "ivanov@example.com",
        };

        // Act
        CreateSupplierResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Id.Should().NotBe(Guid.Empty);

        _repoMock.Verify(
            r => r.Add(It.Is<Supplier>(s => s.Inn == "771234567890")),
            Times.Once);
    }

    // ── Update ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateSupplier_ExistingId_UpdatesAllFields()
    {
        // Arrange
        Supplier existing = BuildSupplier();

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdateSupplierCommandHandler handler =
            new UpdateSupplierCommandHandler(_repoMock.Object);

        UpdateSupplierCommand command = new UpdateSupplierCommand
        {
            Id = existing.Id,
            Inn = "7743099999",
            Name = "ООО Новое Название",
            Description = "Обновлённое описание",
            Phone = "+7 495 999-88-77",
            Email = "new@supplier.ru",
            IsActive = true,
        };

        // Act
        UpdateSupplierResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.Inn.Should().Be("7743099999");
        existing.Name.Should().Be("ООО Новое Название");
        existing.Description.Should().Be("Обновлённое описание");
        existing.Phone.Should().Be("+7 495 999-88-77");
        existing.Email.Should().Be("new@supplier.ru");
        existing.IsActive.Should().BeTrue();
        existing.UpdatedAt.Should().NotBeNull();

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateSupplier_NotFound_ReturnsFalseWithoutSaving()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Supplier?)null);

        UpdateSupplierCommandHandler handler =
            new UpdateSupplierCommandHandler(_repoMock.Object);

        UpdateSupplierCommand command = new UpdateSupplierCommand
        {
            Id = Guid.NewGuid(),
            Inn = "1234567890",
            Name = "Несуществующий",
            IsActive = true,
        };

        // Act
        UpdateSupplierResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateSupplier_CanClearNullableFields()
    {
        // Arrange
        Supplier existing = BuildSupplier();
        existing.Description = "Старое описание";
        existing.Phone = "+7 495 111-22-33";
        existing.Email = "old@example.com";

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdateSupplierCommandHandler handler =
            new UpdateSupplierCommandHandler(_repoMock.Object);

        UpdateSupplierCommand command = new UpdateSupplierCommand
        {
            Id = existing.Id,
            Inn = existing.Inn,
            Name = existing.Name,
            Description = null,
            Phone = null,
            Email = null,
            IsActive = true,
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        existing.Description.Should().BeNull();
        existing.Phone.Should().BeNull();
        existing.Email.Should().BeNull();
    }

    // ── Delete (Soft) ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteSupplier_ExistingSupplier_SoftDeletesAndReturnsFound()
    {
        // Arrange
        Guid id = Guid.NewGuid();
        Supplier existing = BuildSupplier(id, isActive: true);

        _repoMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        DeleteSupplierCommandHandler handler =
            new DeleteSupplierCommandHandler(_repoMock.Object);

        // Act
        DeleteSupplierResponse response = await handler.Handle(
            new DeleteSupplierCommand { Id = id }, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.IsActive.Should().BeFalse();
        existing.UpdatedAt.Should().NotBeNull();

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteSupplier_NotFound_ReturnsFalseWithoutSaving()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Supplier?)null);

        DeleteSupplierCommandHandler handler =
            new DeleteSupplierCommandHandler(_repoMock.Object);

        // Act
        DeleteSupplierResponse response = await handler.Handle(
            new DeleteSupplierCommand { Id = Guid.NewGuid() }, CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteSupplier_AlreadyDeactivated_StillReturnsFound()
    {
        // Arrange
        Guid id = Guid.NewGuid();
        Supplier existing = BuildSupplier(id, isActive: false);
        existing.UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1);

        _repoMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        DeleteSupplierCommandHandler handler =
            new DeleteSupplierCommandHandler(_repoMock.Object);

        DateTimeOffset beforeCall = DateTimeOffset.UtcNow;

        // Act
        DeleteSupplierResponse response = await handler.Handle(
            new DeleteSupplierCommand { Id = id }, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        existing.IsActive.Should().BeFalse();
        existing.UpdatedAt.Should().BeOnOrAfter(beforeCall);
    }

    // ── Queries ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSupplier_ExistingId_ReturnsMappedResponse()
    {
        // Arrange
        Supplier supplier = BuildSupplier();

        _repoMock
            .Setup(r => r.GetByIdAsync(supplier.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplier);

        GetSupplierQueryHandler handler =
            new GetSupplierQueryHandler(_repoMock.Object);

        // Act
        GetSupplierResponse? response = await handler.Handle(
            new GetSupplierQuery { Id = supplier.Id }, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response!.Id.Should().Be(supplier.Id);
        response.Inn.Should().Be(supplier.Inn);
        response.Name.Should().Be(supplier.Name);
        response.Description.Should().Be(supplier.Description);
        response.Phone.Should().Be(supplier.Phone);
        response.Email.Should().Be(supplier.Email);
        response.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetSupplier_MissingId_ReturnsNull()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Supplier?)null);

        GetSupplierQueryHandler handler =
            new GetSupplierQueryHandler(_repoMock.Object);

        // Act
        GetSupplierResponse? response = await handler.Handle(
            new GetSupplierQuery { Id = Guid.NewGuid() }, CancellationToken.None);

        // Assert
        response.Should().BeNull();
    }

    [Fact]
    public async Task GetSuppliers_MultipleSuppliers_ReturnsMappedList()
    {
        // Arrange
        List<Supplier> suppliers = new List<Supplier>
        {
            BuildSupplier(isActive: true),
            BuildSupplier(isActive: false),
            new Supplier
            {
                Id = Guid.NewGuid(),
                Inn = "123456789012",
                Name = "ИП Петров",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            },
        };

        _repoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(suppliers);

        GetSuppliersQueryHandler handler =
            new GetSuppliersQueryHandler(_repoMock.Object);

        // Act
        GetSuppliersResponse response =
            await handler.Handle(new GetSuppliersQuery(), CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(3);
        response.Items.Should().Contain(s => s.Inn == "7743013902");
        response.Items.Should().Contain(s => s.Inn == "123456789012");
        response.Items.Should().Contain(s => s.IsActive == false);
    }

    [Fact]
    public async Task GetSuppliers_EmptyList_ReturnsEmptyCollection()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Supplier>());

        GetSuppliersQueryHandler handler =
            new GetSuppliersQueryHandler(_repoMock.Object);

        // Act
        GetSuppliersResponse response =
            await handler.Handle(new GetSuppliersQuery(), CancellationToken.None);

        // Assert
        response.Items.Should().BeEmpty();
    }
}
