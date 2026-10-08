// GetProductsQueryHandlerTests.cs

using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProducts;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog.Products;

/// <summary>
/// Tests for GetProductsQueryHandler.
/// Validates the opt-in paging contract: a null page reproduces the legacy full-list
/// behaviour, a supplied page drives Skip/Take and clamping, sortBy/sortDir resolve through
/// the handler before reaching the repository, and status/includeCounts are passed through.
/// </summary>
public class GetProductsQueryHandlerTests
{
    private readonly Mock<IProductRepository> _repositoryMock;
    private readonly Mock<IProductPhotoService> _photoServiceMock;
    private readonly GetProductsQueryHandler _handler;

    public GetProductsQueryHandlerTests()
    {
        _repositoryMock = new Mock<IProductRepository>();
        _photoServiceMock = new Mock<IProductPhotoService>();
        _handler = new GetProductsQueryHandler(_repositoryMock.Object, _photoServiceMock.Object);

        _repositoryMock
            .Setup(r => r.GetPagedAsync(
                It.IsAny<ProductStatus?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((0, new List<Product>()));
    }

    [Fact]
    public async Task Handle_NoPage_RequestsFullListWithNoSkipOrTake()
    {
        // Arrange
        List<Product> products = new() { BuildProduct(), BuildProduct(), BuildProduct() };
        _repositoryMock
            .Setup(r => r.GetPagedAsync(
                null, null, null, It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((products.Count, products));

        // Act
        GetProductsResponse response = await _handler.Handle(new GetProductsQuery(), CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(3);
        response.TotalCount.Should().Be(3);
        response.Page.Should().Be(1);

        _repositoryMock.Verify(
            r => r.GetPagedAsync(
                null,
                null,
                null,
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PageAndPageSizeGiven_ComputesSkipAndTake()
    {
        // Arrange: page 2, size 10 -> skip 10, take 10.
        GetProductsQuery query = new() { Page = 2, PageSize = 10 };

        // Act
        await _handler.Handle(query, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.GetPagedAsync(
                null,
                10,
                10,
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10000, 200)]
    [InlineData(-5, 1)]
    public async Task Handle_PageSizeOutOfRange_IsClampedTo1Or200(int requestedPageSize, int expectedPageSize)
    {
        // Arrange
        GetProductsQuery query = new() { Page = 1, PageSize = requestedPageSize };

        // Act
        GetProductsResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.PageSize.Should().Be(expectedPageSize);
        _repositoryMock.Verify(
            r => r.GetPagedAsync(
                null,
                0,
                expectedPageSize,
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Handle_NegativeOrZeroPage_IsTreatedAsPage1(int requestedPage)
    {
        // Arrange
        GetProductsQuery query = new() { Page = requestedPage, PageSize = 50 };

        // Act
        GetProductsResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Page.Should().Be(1);
        _repositoryMock.Verify(
            r => r.GetPagedAsync(
                null,
                0,
                50,
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("sku")]
    [InlineData("status")]
    [InlineData("createdAt")]
    [InlineData("unknownColumn")]
    [InlineData(null)]
    public async Task Handle_SortByIsForwardedVerbatim_RepositoryOwnsTheWhitelist(string? sortBy)
    {
        // The handler does not validate sortBy itself -- resolving against the whitelist
        // (and falling back to name) is the repository's job, exercised by its own tests.
        // Here we only confirm the handler passes the raw value through unchanged.
        GetProductsQuery query = new() { SortBy = sortBy };

        await _handler.Handle(query, CancellationToken.None);

        _repositoryMock.Verify(
            r => r.GetPagedAsync(
                It.IsAny<ProductStatus?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                sortBy,
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("desc", true)]
    [InlineData("DESC", true)]
    [InlineData("asc", false)]
    [InlineData("ASC", false)]
    [InlineData(null, false)]
    [InlineData("sideways", false)]
    public async Task Handle_SortDirResolvesToDescendingFlag_AnythingOtherThanDescIsAscending(
        string? sortDir, bool expectedDescending)
    {
        GetProductsQuery query = new() { SortDir = sortDir };

        await _handler.Handle(query, CancellationToken.None);

        _repositoryMock.Verify(
            r => r.GetPagedAsync(
                It.IsAny<ProductStatus?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                expectedDescending,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_IncludeCountsTrue_PopulatesStatusCountsFromRepository()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetStatusCountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((5, 2, 7));

        GetProductsQuery query = new() { IncludeCounts = true };

        // Act
        GetProductsResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.StatusCounts.Should().NotBeNull();
        response.StatusCounts!.Active.Should().Be(5);
        response.StatusCounts.Archived.Should().Be(2);
        response.StatusCounts.All.Should().Be(7);

        _repositoryMock.Verify(r => r.GetStatusCountsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_IncludeCountsFalse_LeavesStatusCountsNullAndSkipsTheExtraQuery()
    {
        // Arrange
        GetProductsQuery query = new() { IncludeCounts = false };

        // Act
        GetProductsResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.StatusCounts.Should().BeNull();
        _repositoryMock.Verify(r => r.GetStatusCountsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_StatusFilterGiven_IsForwardedToRepository()
    {
        // Arrange
        GetProductsQuery query = new() { Status = ProductStatus.Archived };

        // Act
        await _handler.Handle(query, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.GetPagedAsync(
                ProductStatus.Archived,
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static Product BuildProduct()
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            Name = "Test product",
            Status = ProductStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
