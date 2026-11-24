using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.Products;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Products;
using Delobytes.App.Backend.Catalog.Domain;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Tests.Application.Catalog.Numbering;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Commands;

/// <summary>
/// The manual create and update paths keep the API open for other symbologies but never store a
/// barcode without one: a missing type becomes <see cref="BarcodeTypes.Ean13"/>.
/// </summary>
public class ProductCommandHandlerBarcodeTypeTests
{
    [Fact]
    public async Task Create_TypeNull_StoresEan13()
    {
        // Arrange
        List<Product> added = new List<Product>();
        CreateProductCommandHandler handler = BuildCreateHandler(added);

        CreateProductCommand command = new CreateProductCommand
        {
            Sku = "SKU-001",
            Name = "Product",
            Barcodes = new List<ProductBarcodeDto>
            {
                new ProductBarcodeDto { Value = "4600000000001", IsDefault = true },
            },
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        added.Should().ContainSingle();
        added[0].Barcodes.Should().ContainSingle().Which.Type.Should().Be(BarcodeTypes.Ean13);
    }

    [Fact]
    public async Task Create_ExplicitEan13_IsStoredUnchanged()
    {
        // Arrange
        List<Product> added = new List<Product>();
        CreateProductCommandHandler handler = BuildCreateHandler(added);

        CreateProductCommand command = new CreateProductCommand
        {
            Sku = "SKU-001",
            Name = "Product",
            Barcodes = new List<ProductBarcodeDto>
            {
                new ProductBarcodeDto { Value = "4600000000001", Type = BarcodeTypes.Ean13, IsDefault = true },
            },
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        added[0].Barcodes.Should().ContainSingle().Which.Type.Should().Be(BarcodeTypes.Ean13);
    }

    [Fact]
    public async Task Update_TypeEmpty_StoresEan13()
    {
        // Arrange
        Product product = BuildProductWithBarcode();
        ProductBarcode existing = product.Barcodes.Single();
        Mock<IProductRepository> repositoryMock = new Mock<IProductRepository>();
        repositoryMock
            .Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdateProductCommandHandler handler = new UpdateProductCommandHandler(repositoryMock.Object);

        UpdateProductCommand command = new UpdateProductCommand
        {
            Id = product.Id,
            Barcodes = new List<ProductBarcodeDto>
            {
                new ProductBarcodeDto { Id = existing.Id, Value = existing.Value, Type = "", IsDefault = true },
            },
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        existing.Type.Should().Be(BarcodeTypes.Ean13);
    }

    [Fact]
    public async Task Update_OtherSymbology_IsStoredAsSent()
    {
        // Arrange: the restriction to one option is a UI concern, not a domain invariant.
        Product product = BuildProductWithBarcode();
        ProductBarcode existing = product.Barcodes.Single();
        Mock<IProductRepository> repositoryMock = new Mock<IProductRepository>();
        repositoryMock
            .Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpdateProductCommandHandler handler = new UpdateProductCommandHandler(repositoryMock.Object);

        UpdateProductCommand command = new UpdateProductCommand
        {
            Id = product.Id,
            Barcodes = new List<ProductBarcodeDto>
            {
                new ProductBarcodeDto { Id = existing.Id, Value = existing.Value, Type = "CODE128", IsDefault = true },
            },
        };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        existing.Type.Should().Be("CODE128");
    }

    private static CreateProductCommandHandler BuildCreateHandler(List<Product> added)
    {
        Mock<IProductRepository> repositoryMock = new Mock<IProductRepository>();
        repositoryMock
            .Setup(r => r.SkuExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repositoryMock
            .Setup(r => r.Add(It.IsAny<Product>()))
            .Callback<Product>(added.Add);
        repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        return new CreateProductCommandHandler(
            repositoryMock.Object,
            new FakeNumberingSchemeProvider(),
            new FakeNumberAllocator(),
            new FakeCatalogTransactionFactory());
    }

    private static Product BuildProductWithBarcode()
    {
        Product product = new Product
        {
            Id = Guid.NewGuid(),
            Code = "P-000001",
            Sku = "SKU-001",
            Name = "Product",
            CreationSource = CreationSource.Manual,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };

        product.Barcodes.Add(new ProductBarcode
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Value = "4600000000001",
            Type = BarcodeTypes.Ean13,
            IsDefault = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });

        return product;
    }
}
