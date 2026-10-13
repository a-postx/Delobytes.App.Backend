using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Components.GetComponents;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog.Components;

/// <summary>
/// Tests for GetComponentsQueryHandler.
/// Validates the opt-in paging contract: a null page reproduces the legacy full-list
/// behaviour relied upon by the product BOM editor, a supplied page drives Skip/Take and
/// clamping, and sortBy/sortDir are resolved by the handler before reaching the repository.
/// </summary>
public class GetComponentsQueryHandlerTests
{
    private readonly Mock<IComponentRepository> _repositoryMock;
    private readonly GetComponentsQueryHandler _handler;

    public GetComponentsQueryHandlerTests()
    {
        _repositoryMock = new Mock<IComponentRepository>();
        _handler = new GetComponentsQueryHandler(_repositoryMock.Object);

        _repositoryMock
            .Setup(r => r.GetPagedAsync(
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .ReturnsAsync((0, new List<Component>()));
    }

    [Fact]
    public async Task Handle_NoPage_RequestsFullListWithNoSkipOrTake()
    {
        // Arrange: the product BOM editor relies on componentsApi.getAll() without params
        // returning everything, unpaged.
        List<Component> components = new() { BuildComponent(), BuildComponent(), BuildComponent() };
        _repositoryMock
            .Setup(r => r.GetPagedAsync(
                null,
                null,
                null,
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .ReturnsAsync((components.Count, components));

        // Act
        GetComponentsResponse response = await _handler.Handle(new GetComponentsQuery(), CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(3);
        response.TotalCount.Should().Be(3);
        response.Page.Should().Be(1);

        _repositoryMock.Verify(
            r => r.GetPagedAsync(
                null,
                null,
                null,
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PageAndPageSizeGiven_ComputesSkipAndTake()
    {
        // Arrange: page 2, size 10 -> skip 10, take 10.
        GetComponentsQuery query = new() { Page = 2, PageSize = 10 };

        // Act
        await _handler.Handle(query, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.GetPagedAsync(
                10,
                10,
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()),
            Times.Once);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10000, 200)]
    [InlineData(-5, 1)]
    public async Task Handle_PageSizeOutOfRange_IsClampedTo1Or200(int requestedPageSize, int expectedPageSize)
    {
        // Arrange
        GetComponentsQuery query = new() { Page = 1, PageSize = requestedPageSize };

        // Act
        GetComponentsResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.PageSize.Should().Be(expectedPageSize);
        _repositoryMock.Verify(
            r => r.GetPagedAsync(
                0,
                expectedPageSize,
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()),
            Times.Once);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Handle_NegativeOrZeroPage_IsTreatedAsPage1(int requestedPage)
    {
        // Arrange
        GetComponentsQuery query = new() { Page = requestedPage, PageSize = 50 };

        // Act
        GetComponentsResponse response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.Page.Should().Be(1);
        _repositoryMock.Verify(
            r => r.GetPagedAsync(
                0,
                50,
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()),
            Times.Once);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("supplier")]
    [InlineData("validFrom")]
    [InlineData("createdAt")]
    [InlineData("unknownColumn")]
    [InlineData(null)]
    public async Task Handle_SortByIsForwardedVerbatim_RepositoryOwnsTheWhitelist(string? sortBy)
    {
        // The handler does not validate sortBy itself -- resolving against the whitelist
        // (and falling back to name) is the repository's job, exercised by its own tests.
        GetComponentsQuery query = new() { SortBy = sortBy };

        await _handler.Handle(query, CancellationToken.None);

        _repositoryMock.Verify(
            r => r.GetPagedAsync(
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                sortBy,
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()),
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
        GetComponentsQuery query = new() { SortDir = sortDir };

        await _handler.Handle(query, CancellationToken.None);

        _repositoryMock.Verify(
            r => r.GetPagedAsync(
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                expectedDescending,
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()),
            Times.Once);
    }

    [Theory]
    [InlineData("bolt")]
    [InlineData("  bolt  ")]
    [InlineData(null)]
    public async Task Handle_SearchIsForwardedVerbatim(string? search)
    {
        // The handler does not trim or otherwise rewrite the term -- normalisation lives in the
        // repository.
        GetComponentsQuery query = new() { Search = search };

        await _handler.Handle(query, CancellationToken.None);

        _repositoryMock.Verify(
            r => r.GetPagedAsync(
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>(),
                search),
            Times.Once);
    }

    [Fact]
    public async Task Handle_MapsActivePriceAndDescriptiveFields()
    {
        // Arrange
        Guid supplierId = Guid.NewGuid();
        Component component = new Component
        {
            Id = Guid.NewGuid(),
            Name = "Bolt M6",
            Description = "Steel bolt",
            Unit = Unit.Piece,
            Category = ComponentCategory.Material,
            IsActive = true,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        component.Prices.Add(new ComponentPrice
        {
            Id = Guid.NewGuid(),
            ComponentId = component.Id,
            PricePerUnit = 12.5m,
            SupplierId = supplierId,
            Supplier = new Supplier { Id = supplierId, Inn = "1111111111", Name = "Acme", IsActive = true },
            ValidFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        _repositoryMock
            .Setup(r => r.GetPagedAsync(
                null,
                null,
                null,
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .ReturnsAsync((1, new List<Component> { component }));

        // Act
        GetComponentsResponse response = await _handler.Handle(new GetComponentsQuery(), CancellationToken.None);

        // Assert
        ComponentItem item = response.Items.Should().ContainSingle().Subject;
        item.Name.Should().Be("Bolt M6");
        item.ActivePrice.Should().NotBeNull();
        item.ActivePrice!.PricePerUnit.Should().Be(12.5m);
    }

    private static Component BuildComponent()
    {
        return new Component
        {
            Id = Guid.NewGuid(),
            Name = "Test component " + Guid.NewGuid().ToString("N").Substring(0, 8),
            Unit = Unit.Piece,
            Category = ComponentCategory.Material,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
