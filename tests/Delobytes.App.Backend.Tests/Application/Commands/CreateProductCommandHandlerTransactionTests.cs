using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.Products;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Numbering;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Tests.Application.Catalog.Numbering;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Commands;

/// <summary>
/// The manual create owns a transaction around "allocate a number, check the SKU, insert" because a
/// number may stay durable only while the product does. These tests assert the call order, not
/// merely that each call happened: allocating before the transaction or committing before the
/// insert would satisfy the weaker assertion and break the invariant.
/// </summary>
public class CreateProductCommandHandlerTransactionTests
{
    [Fact]
    public async Task Handle_SuccessfulCreate_AllocatesInsideTheTransactionAndCommitsAfterSave()
    {
        // Arrange
        FakeCatalogTransactionFactory transactionFactory = new FakeCatalogTransactionFactory();
        Mock<INumberAllocator> allocatorMock = BuildAllocator(transactionFactory.CallLog);
        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(transactionFactory.CallLog, saveException: null);

        CreateProductCommandHandler handler = new CreateProductCommandHandler(
            repositoryMock.Object,
            new FakeNumberingSchemeProvider(),
            allocatorMock.Object,
            transactionFactory);

        // Act
        await handler.Handle(BuildCommand(), CancellationToken.None);

        // Assert
        transactionFactory.CallLog.Should().ContainInOrder("begin", "allocate", "save", "commit");
        transactionFactory.CallLog.Should().NotContain("rollback");
    }

    [Fact]
    public async Task Handle_AllocatorThrows_RollsBackAndAddsNothing()
    {
        // Arrange
        FakeCatalogTransactionFactory transactionFactory = new FakeCatalogTransactionFactory();
        Mock<INumberAllocator> allocatorMock = new Mock<INumberAllocator>();
        allocatorMock
            .Setup(a => a.AllocateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => transactionFactory.CallLog.Add("allocate"))
            .ThrowsAsync(new InvalidOperationException("numbering is inactive"));

        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(transactionFactory.CallLog, saveException: null);

        CreateProductCommandHandler handler = new CreateProductCommandHandler(
            repositoryMock.Object,
            new FakeNumberingSchemeProvider(),
            allocatorMock.Object,
            transactionFactory);

        // Act
        Func<Task> act = () => handler.Handle(BuildCommand(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        transactionFactory.CallLog.Should().Contain("rollback");
        transactionFactory.CallLog.Should().NotContain("commit");
        repositoryMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SaveThrowsSkuConflict_RollsBackAndPropagatesTheExceptionUnchanged()
    {
        // Arrange: the unique index is the backstop of the pre-check, so the translated 409 must
        // reach the caller untouched — including its error code.
        FakeCatalogTransactionFactory transactionFactory = new FakeCatalogTransactionFactory();
        AppException conflict = new AppException(ErrorCodes.Catalog.ProductSkuConflict);

        Mock<IProductRepository> repositoryMock = BuildRepositoryMock(transactionFactory.CallLog, saveException: conflict);

        CreateProductCommandHandler handler = new CreateProductCommandHandler(
            repositoryMock.Object,
            new FakeNumberingSchemeProvider(),
            new FakeNumberAllocator(),
            transactionFactory);

        // Act
        Func<Task> act = () => handler.Handle(BuildCommand(), CancellationToken.None);

        // Assert
        AppException thrown = (await act.Should().ThrowAsync<AppException>()).Which;
        thrown.Should().BeSameAs(conflict);
        thrown.Code.Should().Be(ErrorCodes.Catalog.ProductSkuConflict);

        transactionFactory.CallLog.Should().Contain("rollback");
        transactionFactory.CallLog.Should().NotContain("commit");
    }

    private static CreateProductCommand BuildCommand()
    {
        return new CreateProductCommand
        {
            Sku = "SKU-001",
            Name = "Created product",
        };
    }

    private static Mock<INumberAllocator> BuildAllocator(List<string> callLog)
    {
        Mock<INumberAllocator> allocatorMock = new Mock<INumberAllocator>();
        allocatorMock
            .Setup(a => a.AllocateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => callLog.Add("allocate"))
            .ReturnsAsync("P-000001");

        return allocatorMock;
    }

    private static Mock<IProductRepository> BuildRepositoryMock(List<string> callLog, Exception? saveException)
    {
        Mock<IProductRepository> repositoryMock = new Mock<IProductRepository>();
        repositoryMock
            .Setup(r => r.SkuExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repositoryMock
            .Setup(r => r.Add(It.IsAny<Product>()));

        if (saveException == null)
        {
            repositoryMock
                .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Callback(() => callLog.Add("save"))
                .ReturnsAsync(1);
        }
        else
        {
            repositoryMock
                .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Callback(() => callLog.Add("save"))
                .ThrowsAsync(saveException);
        }

        return repositoryMock;
    }
}
