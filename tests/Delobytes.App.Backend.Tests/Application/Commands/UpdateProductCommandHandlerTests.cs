using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.Products.UpdateProduct;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Products;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Commands;

/// <summary>
/// Tests for UpdateProductCommandHandler and UpdateProductCommandValidator.
///
/// The behaviour under test is the partial-update contract: an omitted field stays untouched, so
/// the SKU of a marketplace-linked product can be renamed without the form having to resend --
/// and therefore risk overwriting -- the Name/Description/Barcodes/PackingUnit that the import
/// owns.
/// </summary>
public class UpdateProductCommandHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task Handle_SkuOnly_LeavesEveryOtherFieldUntouched()
    {
        // Arrange: the linked-product case -- the form sends only the SKU.
        Product product = BuildProduct();
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(product);
        UpdateProductCommandHandler handler = new UpdateProductCommandHandler(repositoryMock.Object);

        UpdateProductCommand command = new UpdateProductCommand
        {
            Id = product.Id,
            Sku = "NEW-SKU",
        };

        // Act
        UpdateProductResponse response = await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        product.Sku.Should().Be("NEW-SKU");
        product.Name.Should().Be("Original name");
        product.Description.Should().Be("Original description");
        product.Barcodes.Should().HaveCount(1);
        product.Barcodes.Should().ContainSingle(b => b.Value == "4600000000001");
        product.PackingUnits.Should().HaveCount(1);
        product.PackingUnits.Should().ContainSingle(pu => pu.LengthCm == 10m);

        repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SkuOnly_DoesNotClearBarcodes()
    {
        // Arrange: Barcodes == null must not be read as "the user cleared the list".
        // The previous contract treated null and [] as equivalent, which would have wiped every
        // barcode of a linked product on an SKU-only save.
        Product product = BuildProduct();
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(product);
        UpdateProductCommandHandler handler = new UpdateProductCommandHandler(repositoryMock.Object);

        // Act
        await handler.Handle(
            new UpdateProductCommand { Id = product.Id, Sku = "NEW-SKU" },
            CancellationToken.None);

        // Assert
        product.Barcodes.Should().NotBeEmpty();
        product.Barcodes.Should().ContainSingle(b => b.Value == "4600000000001");
    }

    [Fact]
    public async Task Handle_SkuIsTrimmed()
    {
        // Arrange
        Product product = BuildProduct();
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(product);
        UpdateProductCommandHandler handler = new UpdateProductCommandHandler(repositoryMock.Object);

        // Act
        await handler.Handle(
            new UpdateProductCommand { Id = product.Id, Sku = "  NEW-SKU  " },
            CancellationToken.None);

        // Assert: the unique index compares exact strings, so stray whitespace would otherwise
        // create a second SKU that looks identical to a human.
        product.Sku.Should().Be("NEW-SKU");
    }

    [Fact]
    public async Task Handle_NameUnchangedWhenSkuOnlyUpdate()
    {
        // Arrange
        Product product = BuildProduct();
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(product);
        UpdateProductCommandHandler handler = new UpdateProductCommandHandler(repositoryMock.Object);

        // Act
        await handler.Handle(
            new UpdateProductCommand { Id = product.Id, Name = "Renamed" },
            CancellationToken.None);

        // Assert
        product.Name.Should().Be("Renamed");
        product.Sku.Should().Be("OLD-SKU");
    }

    [Fact]
    public async Task Handle_DescriptionSentAsEmptyString_ClearsIt()
    {
        // Arrange: an empty string is a value, not an omission -- it is how the form clears the
        // field. Only null means "leave as is".
        Product product = BuildProduct();
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(product);
        UpdateProductCommandHandler handler = new UpdateProductCommandHandler(repositoryMock.Object);

        // Act
        await handler.Handle(
            new UpdateProductCommand { Id = product.Id, Description = "   " },
            CancellationToken.None);

        // Assert
        product.Description.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFoundAndDoesNotSave()
    {
        // Arrange
        Product product = BuildProduct();
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(product);
        UpdateProductCommandHandler handler = new UpdateProductCommandHandler(repositoryMock.Object);

        // Act
        UpdateProductResponse response = await handler.Handle(
            new UpdateProductCommand { Id = Guid.NewGuid(), Sku = "NEW-SKU" },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        product.Sku.Should().Be("OLD-SKU");
        repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DeletedProduct_ReturnsNotFoundAndDoesNotSave()
    {
        // Arrange
        Product product = BuildProduct(ProductStatus.Deleted);
        product.DeletedAt = DateTimeOffset.UtcNow;
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(product);
        UpdateProductCommandHandler handler = new UpdateProductCommandHandler(repositoryMock.Object);

        // Act
        UpdateProductResponse response = await handler.Handle(
            new UpdateProductCommand { Id = product.Id, Sku = "NEW-SKU" },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        product.Sku.Should().Be("OLD-SKU");
        repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AllFieldsSupplied_WritesAllOfThem()
    {
        // Arrange: the manual-product case.
        Product product = BuildProduct();
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(product);
        UpdateProductCommandHandler handler = new UpdateProductCommandHandler(repositoryMock.Object);

        UpdateProductCommand command = new UpdateProductCommand
        {
            Id = product.Id,
            Sku = "NEW-SKU",
            Name = "New name",
            Description = "New description",
            Barcodes = new List<ProductBarcodeDto>
            {
                new ProductBarcodeDto { Value = "1111111111111", IsDefault = true },
            },
            PackingUnit = new PackingUnitDto
            {
                LengthCm = 20m,
                WidthCm = 30m,
                HeightCm = 40m,
                WeightKg = 2m,
            },
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        product.Sku.Should().Be("NEW-SKU");
        product.Name.Should().Be("New name");
        product.Description.Should().Be("New description");
        product.Barcodes.Should().ContainSingle(b => b.Value == "1111111111111");
        product.PackingUnits.Should().ContainSingle(pu => pu.LengthCm == 20m && pu.WeightKg == 2m);
    }

    [Fact]
    public void Validator_RejectsEmptySku()
    {
        // Arrange: an empty SKU is rejected rather than read as "clear the SKU" -- the column is
        // required, and the field is the whole point of the form.
        UpdateProductCommandValidator validator = new UpdateProductCommandValidator();

        // Act
        FluentValidation.Results.ValidationResult result = validator.Validate(
            new UpdateProductCommand { Id = Guid.NewGuid(), Sku = "   " });

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateProductCommand.Sku));
    }

    [Fact]
    public void Validator_AllowsAbsentSku()
    {
        // Arrange: null means "field not sent", which a partial update must accept.
        UpdateProductCommandValidator validator = new UpdateProductCommandValidator();

        // Act
        FluentValidation.Results.ValidationResult result = validator.Validate(
            new UpdateProductCommand { Id = Guid.NewGuid(), Name = "Only the name" });

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_RejectsSkuLongerThanColumnLimit()
    {
        // Arrange: mirrors ProductConfiguration Sku HasMaxLength(100).
        UpdateProductCommandValidator validator = new UpdateProductCommandValidator();

        // Act
        FluentValidation.Results.ValidationResult result = validator.Validate(
            new UpdateProductCommand { Id = Guid.NewGuid(), Sku = new string('X', 101) });

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateProductCommand.Sku));
    }

    [Fact]
    public void Validator_RejectsEmptyName()
    {
        // Arrange
        UpdateProductCommandValidator validator = new UpdateProductCommandValidator();

        // Act
        FluentValidation.Results.ValidationResult result = validator.Validate(
            new UpdateProductCommand { Id = Guid.NewGuid(), Name = string.Empty });

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateProductCommand.Name));
    }

    private static Product BuildProduct(ProductStatus status = ProductStatus.Active)
    {
        Product product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "OLD-SKU",
            Name = "Original name",
            Description = "Original description",
            Status = status,
            CreationSource = CreationSource.WildberriesImport,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
        };

        product.Barcodes.Add(new ProductBarcode
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Value = "4600000000001",
            Type = "wildberries",
            IsDefault = true,
        });

        product.PackingUnits.Add(new PackingUnit
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            LengthCm = 10m,
            WidthCm = 15m,
            HeightCm = 20m,
            WeightKg = 1m,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
        });

        return product;
    }

    private static Mock<IProductRepository> BuildRepositoryMock(Product product)
    {
        Mock<IProductRepository> repositoryMock = new Mock<IProductRepository>();

        repositoryMock
            .Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        return repositoryMock;
    }
}
