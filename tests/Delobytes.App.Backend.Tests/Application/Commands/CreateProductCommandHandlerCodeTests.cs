using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Api.Controllers;
using Delobytes.App.Backend.Catalog.Application.Commands.Products;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Numbering;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Tests.Application.Catalog.Numbering;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Commands;

/// <summary>
/// Manual creation must issue an immutable product code from the default numbering scheme.
/// The code is never accepted from the client: neither the request body nor the command carries it.
/// </summary>
public class CreateProductCommandHandlerCodeTests
{
    [Fact]
    public async Task Handle_Create_AssignsCodeFromTheNumberingScheme()
    {
        // Arrange
        List<Product> added = new List<Product>();
        CreateProductCommandHandler handler = BuildHandler(added);

        // Act
        CreateProductResponse response = await handler.Handle(BuildCommand("SKU-001"), CancellationToken.None);

        // Assert
        added.Should().ContainSingle();
        response.Code.Should().MatchRegex("^P-\\d{6}$");
        added[0].Code.Should().Be(response.Code);
        added[0].Sku.Should().Be("SKU-001");
    }

    [Fact]
    public async Task Handle_TwoConsecutiveCreates_ReceiveDifferentCodes()
    {
        // Arrange
        List<Product> added = new List<Product>();
        CreateProductCommandHandler handler = BuildHandler(added);

        // Act
        CreateProductResponse first = await handler.Handle(BuildCommand("SKU-001"), CancellationToken.None);
        CreateProductResponse second = await handler.Handle(BuildCommand("SKU-002"), CancellationToken.None);

        // Assert
        first.Code.Should().NotBe(second.Code);
        added.Select(p => p.Code).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void CreatePayloads_ExposeNoCodeProperty()
    {
        // Both the wire contract and the command must stay closed to a client-supplied code:
        // a code is issued by the scheme and is immutable afterwards.
        typeof(CreateProductRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Should().NotContain("Code");

        typeof(CreateProductCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Should().NotContain("Code");
    }

    [Fact]
    public async Task Handle_AllocatorThrows_NothingIsAddedToTheRepository()
    {
        // Arrange: a number that never became durable must not leave a product behind.
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(new List<Product>());
        FakeNumberingSchemeProvider schemeProvider = new FakeNumberingSchemeProvider();
        FakeNumberAllocator allocator = new FakeNumberAllocator
        {
            ExceptionToThrow = new InvalidOperationException("numbering is unavailable"),
        };

        CreateProductCommandHandler handler = new CreateProductCommandHandler(
            repositoryMock.Object,
            schemeProvider,
            allocator,
            new FakeCatalogTransactionFactory());

        // Act
        Func<Task> act = () => handler.Handle(BuildCommand("SKU-001"), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*numbering is unavailable*");
        repositoryMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Never);
        repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CreateProductCommand BuildCommand(string sku)
    {
        return new CreateProductCommand
        {
            Sku = sku,
            Name = "Created product",
        };
    }

    private static CreateProductCommandHandler BuildHandler(List<Product> added)
    {
        return new CreateProductCommandHandler(
            BuildRepositoryMock(added).Object,
            new FakeNumberingSchemeProvider(),
            new FakeNumberAllocator(),
            new FakeCatalogTransactionFactory());
    }

    private static Mock<IProductRepository> BuildRepositoryMock(List<Product> added)
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

        return repositoryMock;
    }
}
