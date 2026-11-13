using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.Products;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Tests.Application.Catalog.Numbering;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Commands;

/// <summary>
/// Manual creation resolves SKU uniqueness in code and answers a taken SKU with the same 409 the
/// update path already returns, instead of letting the unique index surface as a provider error.
/// </summary>
public class CreateProductCommandHandlerSkuTests
{
    [Fact]
    public async Task Handle_SkuAlreadyExists_ThrowsSkuConflictAndInsertsNothing()
    {
        // Arrange
        List<Product> added = new List<Product>();
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(added, skuExists: true);
        CreateProductCommandHandler handler = BuildHandler(repositoryMock);

        // Act
        Func<Task> act = () => handler.Handle(BuildCommand("TAKEN-SKU"), CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<AppException>())
            .Which.Code.Should().Be(ErrorCodes.Catalog.ProductSkuConflict);

        repositoryMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Never);
        repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        added.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SkuIsFree_StoresItUnchanged()
    {
        // Arrange
        List<Product> added = new List<Product>();
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(added, skuExists: false);
        CreateProductCommandHandler handler = BuildHandler(repositoryMock);

        // Act
        await handler.Handle(BuildCommand("FREE-SKU"), CancellationToken.None);

        // Assert
        added.Should().ContainSingle();
        added[0].Sku.Should().Be("FREE-SKU");
        repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static CreateProductCommand BuildCommand(string sku)
    {
        return new CreateProductCommand
        {
            Sku = sku,
            Name = "Created product",
        };
    }

    private static CreateProductCommandHandler BuildHandler(Mock<IProductRepository> repositoryMock)
    {
        return new CreateProductCommandHandler(
            repositoryMock.Object,
            new FakeNumberingSchemeProvider(),
            new FakeNumberAllocator(),
            new FakeCatalogTransactionFactory());
    }

    private static Mock<IProductRepository> BuildRepositoryMock(List<Product> added, bool skuExists)
    {
        Mock<IProductRepository> repositoryMock = new Mock<IProductRepository>();
        repositoryMock
            .Setup(r => r.SkuExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(skuExists);
        repositoryMock
            .Setup(r => r.Add(It.IsAny<Product>()))
            .Callback<Product>(added.Add);
        repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        return repositoryMock;
    }
}
