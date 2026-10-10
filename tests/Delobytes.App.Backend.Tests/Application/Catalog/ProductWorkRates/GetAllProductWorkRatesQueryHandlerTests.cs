using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.ProductWorkRates;
using Delobytes.App.Backend.Catalog.Application.Queries.ProductWorkRates.GetAllProductWorkRates;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog.ProductWorkRates;

/// <summary>
/// Tests for GetAllProductWorkRatesQueryHandler.
/// Validates the product-level paging contract: a null page keeps the legacy full-list behaviour,
/// a supplied page drives Skip/Take over products, out-of-range page sizes are clamped, the status
/// filter and search term are passed through unchanged, and includeCounts adds group-level totals.
/// </summary>
public class GetAllProductWorkRatesQueryHandlerTests
{
    private readonly Mock<IProductWorkRateRepository> _repositoryMock;
    private readonly GetAllProductWorkRatesQueryHandler _handler;

    public GetAllProductWorkRatesQueryHandlerTests()
    {
        _repositoryMock = new Mock<IProductWorkRateRepository>();
        _handler = new GetAllProductWorkRatesQueryHandler(_repositoryMock.Object);

        _repositoryMock
            .Setup(r => r.GetPagedByProductAsync(
                It.IsAny<ProductWorkRateGroupFilter>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((0, new List<ProductWorkRate>()));

        _repositoryMock
            .Setup(r => r.GetGroupCountsAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((0, 0, 0));
    }

    [Fact]
    public async Task Handle_NoPage_RequestsTheFullListWithNoSkipOrTake()
    {
        // Arrange
        List<ProductWorkRate> rates = new() { BuildRate(), BuildRate(), BuildRate() };

        _repositoryMock
            .Setup(r => r.GetPagedByProductAsync(
                It.IsAny<ProductWorkRateGroupFilter>(),
                It.IsAny<string?>(),
                null,
                null,
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((3, rates));

        // Act
        GetAllProductWorkRatesResponse response = await _handler.Handle(
            new GetAllProductWorkRatesQuery(),
            CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(3);
        response.TotalCount.Should().Be(3);
        response.Page.Should().Be(1);

        _repositoryMock.Verify(
            r => r.GetPagedByProductAsync(
                It.IsAny<ProductWorkRateGroupFilter>(),
                It.IsAny<string?>(),
                null,
                null,
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PageAndPageSizeGiven_ComputesSkipAndTakeInProducts()
    {
        // Arrange: page 3 of 25 products skips 50 products; the skip counts products, not versions.
        GetAllProductWorkRatesQuery query = new() { Page = 3, PageSize = 25 };

        // Act
        await _handler.Handle(query, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.GetPagedByProductAsync(
                It.IsAny<ProductWorkRateGroupFilter>(),
                It.IsAny<string?>(),
                50,
                25,
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PageWithoutPageSize_AppliesTheDefaultOf25()
    {
        // Arrange
        GetAllProductWorkRatesQuery query = new() { Page = 1 };

        // Act
        GetAllProductWorkRatesResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.PageSize.Should().Be(25);
        _repositoryMock.Verify(
            r => r.GetPagedByProductAsync(
                It.IsAny<ProductWorkRateGroupFilter>(),
                It.IsAny<string?>(),
                0,
                25,
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
        GetAllProductWorkRatesQuery query = new() { Page = 1, PageSize = requestedPageSize };

        // Act
        GetAllProductWorkRatesResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.PageSize.Should().Be(expectedPageSize);
        _repositoryMock.Verify(
            r => r.GetPagedByProductAsync(
                It.IsAny<ProductWorkRateGroupFilter>(),
                It.IsAny<string?>(),
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
        GetAllProductWorkRatesQuery query = new() { Page = requestedPage, PageSize = 25 };

        // Act
        GetAllProductWorkRatesResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Page.Should().Be(1);
        _repositoryMock.Verify(
            r => r.GetPagedByProductAsync(
                It.IsAny<ProductWorkRateGroupFilter>(),
                It.IsAny<string?>(),
                0,
                25,
                It.IsAny<string?>(),
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
        // Arrange
        GetAllProductWorkRatesQuery query = new() { SortDir = sortDir };

        // Act
        await _handler.Handle(query, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.GetPagedByProductAsync(
                It.IsAny<ProductWorkRateGroupFilter>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                expectedDescending,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("productName")]
    [InlineData("updatedAt")]
    [InlineData("validFrom")]
    [InlineData("unknownColumn")]
    [InlineData(null)]
    public async Task Handle_SortByIsForwardedVerbatim_RepositoryOwnsTheWhitelist(string? sortBy)
    {
        // The handler does not resolve sortBy itself -- the whitelist (and the fallback to
        // productName) lives in the repository, exercised by its own tests.
        GetAllProductWorkRatesQuery query = new() { SortBy = sortBy };

        await _handler.Handle(query, CancellationToken.None);

        _repositoryMock.Verify(
            r => r.GetPagedByProductAsync(
                It.IsAny<ProductWorkRateGroupFilter>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                sortBy,
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_DefaultRequest_FiltersActiveGroups()
    {
        // The default differs from the product list, where a missing status means "all statuses":
        // the page opens on the active groups, and the filter is applied by the query, not the client.
        await _handler.Handle(new GetAllProductWorkRatesQuery(), CancellationToken.None);

        _repositoryMock.Verify(
            r => r.GetPagedByProductAsync(
                ProductWorkRateGroupFilter.Active,
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(ProductWorkRateGroupFilter.Active)]
    [InlineData(ProductWorkRateGroupFilter.All)]
    [InlineData(ProductWorkRateGroupFilter.Inactive)]
    public async Task Handle_StatusFilter_IsForwardedUnchanged(ProductWorkRateGroupFilter status)
    {
        GetAllProductWorkRatesQuery query = new() { Status = status };

        await _handler.Handle(query, CancellationToken.None);

        _repositoryMock.Verify(
            r => r.GetPagedByProductAsync(
                status,
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("widget")]
    [InlineData("  widget  ")]
    public async Task Handle_Search_IsForwardedUnchanged(string search)
    {
        // Trimming and the length cap belong to the repository, which normalises the term the same
        // way the product list does.
        GetAllProductWorkRatesQuery query = new() { Search = search };

        await _handler.Handle(query, CancellationToken.None);

        _repositoryMock.Verify(
            r => r.GetPagedByProductAsync(
                It.IsAny<ProductWorkRateGroupFilter>(),
                search,
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_IncludeCountsTrue_PopulatesGroupCountsFromRepository()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetGroupCountsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((5, 2, 6));

        GetAllProductWorkRatesQuery query = new() { IncludeCounts = true };

        // Act
        GetAllProductWorkRatesResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.StatusCounts.Should().NotBeNull();
        response.StatusCounts!.Active.Should().Be(5);
        response.StatusCounts.Inactive.Should().Be(2);
        response.StatusCounts.All.Should().Be(6);
    }

    [Fact]
    public async Task Handle_IncludeCountsFalse_LeavesGroupCountsNullAndSkipsTheCountQuery()
    {
        // Act
        GetAllProductWorkRatesResponse response = await _handler.Handle(
            new GetAllProductWorkRatesQuery(),
            CancellationToken.None);

        // Assert
        response.StatusCounts.Should().BeNull();

        _repositoryMock.Verify(
            r => r.GetGroupCountsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_IncludeCountsWithSearch_PassesTheSameSearchToTheCounters()
    {
        // The counters must describe the searched result set, otherwise the filter labels advertise
        // totals the user cannot reach.
        GetAllProductWorkRatesQuery query = new() { Search = "widget", IncludeCounts = true };

        await _handler.Handle(query, CancellationToken.None);

        _repositoryMock.Verify(
            r => r.GetGroupCountsAsync("widget", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_IncludeCountsTrue_StillReturnsThePageItems()
    {
        // A response rebuilt for the counters must not drop the page it already had.
        List<ProductWorkRate> rates = new() { BuildRate(), BuildRate() };

        _repositoryMock
            .Setup(r => r.GetPagedByProductAsync(
                It.IsAny<ProductWorkRateGroupFilter>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((2, rates));

        GetAllProductWorkRatesQuery query = new() { Page = 1, PageSize = 25, IncludeCounts = true };

        // Act
        GetAllProductWorkRatesResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(2);
        response.TotalCount.Should().Be(2);
        response.PageSize.Should().Be(25);
    }

    [Fact]
    public async Task Handle_RateWithProduct_MapsProductNameAndSku()
    {
        // The client no longer resolves names itself, so the DTO has to carry them.
        Product product = BuildProduct("Blue Widget", "SKU-1");
        ProductWorkRate rate = BuildRate(product);

        _repositoryMock
            .Setup(r => r.GetPagedByProductAsync(
                It.IsAny<ProductWorkRateGroupFilter>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((1, new List<ProductWorkRate> { rate }));

        // Act
        GetAllProductWorkRatesResponse response = await _handler.Handle(
            new GetAllProductWorkRatesQuery(),
            CancellationToken.None);

        // Assert
        ProductWorkRateDto item = response.Items.Should().ContainSingle().Which;
        item.ProductId.Should().Be(product.Id);
        item.ProductName.Should().Be("Blue Widget");
        item.ProductSku.Should().Be("SKU-1");
        item.ValidFrom.Should().Be(new DateOnly(2026, 3, 12));
        item.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_EmptyResult_ReturnsZeroCountAndNoItems()
    {
        // Act
        GetAllProductWorkRatesResponse response = await _handler.Handle(
            new GetAllProductWorkRatesQuery { Page = 1 },
            CancellationToken.None);

        // Assert
        response.Items.Should().BeEmpty();
        response.TotalCount.Should().Be(0);
    }

    private static Product BuildProduct(string name, string sku)
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = sku,
            Name = name,
            Status = ProductStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static ProductWorkRate BuildRate(Product? product = null)
    {
        Product owner = product ?? BuildProduct("Product", "SKU-0");

        return new ProductWorkRate
        {
            Id = Guid.NewGuid(),
            ProductId = owner.Id,
            Product = owner,
            WorkRateId = Guid.NewGuid(),
            AssemblyRatePerDay = 8,
            ValidFrom = new DateOnly(2026, 3, 12),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
