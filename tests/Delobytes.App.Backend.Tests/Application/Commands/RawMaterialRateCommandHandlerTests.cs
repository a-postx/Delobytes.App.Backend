using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.RawMaterialRates.CreateRawMaterialRate;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.RawMaterialRates.GetRawMaterialRates;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

/// <summary>
/// Unit tests for RawMaterialRate handlers.
/// Key invariant: creation always appends a new versioned record;
/// previous records must never be mutated.
/// </summary>
public class RawMaterialRateCommandHandlerTests
{
    private readonly Mock<IRawMaterialRateRepository> _repoMock = new();

    private static RawMaterialRate BuildRate(Guid productId, DateOnly validFrom, decimal cost)
        => new RawMaterialRate
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            CostPerUnit = cost,
            ValidFrom = validFrom,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // ── Create ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRawMaterialRate_ValidCommand_AddsNewRecordAndReturnsId()
    {
        // Arrange
        Guid productId = Guid.NewGuid();

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateRawMaterialRateCommandHandler handler =
            new CreateRawMaterialRateCommandHandler(_repoMock.Object);

        CreateRawMaterialRateCommand command = new CreateRawMaterialRateCommand
        {
            ProductId = productId,
            CostPerUnit = 150.75m,
            ValidFrom = new DateOnly(2026, 4, 1),
        };

        // Act
        CreateRawMaterialRateResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Id.Should().NotBe(Guid.Empty);

        _repoMock.Verify(
            r => r.Add(It.Is<RawMaterialRate>(rate =>
                rate.ProductId == productId &&
                rate.CostPerUnit == 150.75m &&
                rate.ValidFrom == new DateOnly(2026, 4, 1))),
            Times.Once);

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateRawMaterialRate_TwiceForSameProduct_AddsTwoSeparateRecords()
    {
        // Each call should result in an independent Add — versioning means no update/replace.

        // Arrange
        Guid productId = Guid.NewGuid();
        List<RawMaterialRate> added = new List<RawMaterialRate>();

        _repoMock
            .Setup(r => r.Add(It.IsAny<RawMaterialRate>()))
            .Callback<RawMaterialRate>(r => added.Add(r));

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateRawMaterialRateCommandHandler handler =
            new CreateRawMaterialRateCommandHandler(_repoMock.Object);

        CreateRawMaterialRateCommand v1 = new CreateRawMaterialRateCommand
        {
            ProductId = productId,
            CostPerUnit = 100m,
            ValidFrom = new DateOnly(2026, 1, 1),
        };

        CreateRawMaterialRateCommand v2 = new CreateRawMaterialRateCommand
        {
            ProductId = productId,
            CostPerUnit = 120m,
            ValidFrom = new DateOnly(2026, 6, 1),
        };

        // Act
        CreateRawMaterialRateResponse r1 = await handler.Handle(v1, CancellationToken.None);
        CreateRawMaterialRateResponse r2 = await handler.Handle(v2, CancellationToken.None);

        // Assert — two distinct records, ids differ
        r1.Id.Should().NotBe(r2.Id);

        added.Should().HaveCount(2);
        added.Should().Contain(r => r.CostPerUnit == 100m && r.ValidFrom == new DateOnly(2026, 1, 1));
        added.Should().Contain(r => r.CostPerUnit == 120m && r.ValidFrom == new DateOnly(2026, 6, 1));

        // Previous record must not have been mutated (no update call expected)
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CreateRawMaterialRate_CreatedAtIsSetToUtcNow()
    {
        // Arrange
        RawMaterialRate? captured = null;
        DateTimeOffset before = DateTimeOffset.UtcNow;

        _repoMock
            .Setup(r => r.Add(It.IsAny<RawMaterialRate>()))
            .Callback<RawMaterialRate>(r => captured = r);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateRawMaterialRateCommandHandler handler =
            new CreateRawMaterialRateCommandHandler(_repoMock.Object);

        // Act
        await handler.Handle(new CreateRawMaterialRateCommand
        {
            ProductId = Guid.NewGuid(),
            CostPerUnit = 200m,
            ValidFrom = new DateOnly(2026, 1, 1),
        }, CancellationToken.None);

        DateTimeOffset after = DateTimeOffset.UtcNow;

        // Assert
        captured.Should().NotBeNull();
        captured!.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // ── GetByProduct ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetRawMaterialRates_ExistingProduct_ReturnsVersionsOrderedByValidFrom()
    {
        // Arrange
        Guid productId = Guid.NewGuid();

        List<RawMaterialRate> rates = new List<RawMaterialRate>
        {
            // Repo returns descending order (as implemented)
            BuildRate(productId, new DateOnly(2026, 6, 1), 120m),
            BuildRate(productId, new DateOnly(2026, 1, 1), 100m),
        };

        _repoMock
            .Setup(r => r.GetByProductIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rates);

        GetRawMaterialRatesQueryHandler handler =
            new GetRawMaterialRatesQueryHandler(_repoMock.Object);

        // Act
        GetRawMaterialRatesResponse response = await handler.Handle(
            new GetRawMaterialRatesQuery { ProductId = productId },
            CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(2);
        response.Items.Should().OnlyContain(i => i.ProductId == productId);
        response.Items[0].ValidFrom.Should().Be(new DateOnly(2026, 6, 1));
        response.Items[1].ValidFrom.Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public async Task GetRawMaterialRates_NoRatesForProduct_ReturnsEmptyList()
    {
        // Arrange
        Guid productId = Guid.NewGuid();

        _repoMock
            .Setup(r => r.GetByProductIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RawMaterialRate>());

        GetRawMaterialRatesQueryHandler handler =
            new GetRawMaterialRatesQueryHandler(_repoMock.Object);

        // Act
        GetRawMaterialRatesResponse response = await handler.Handle(
            new GetRawMaterialRatesQuery { ProductId = productId },
            CancellationToken.None);

        // Assert
        response.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRawMaterialRates_QueriesOnlySpecifiedProduct()
    {
        // Only the requested productId should be passed to the repo.

        // Arrange
        Guid productId = Guid.NewGuid();

        _repoMock
            .Setup(r => r.GetByProductIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RawMaterialRate>());

        GetRawMaterialRatesQueryHandler handler =
            new GetRawMaterialRatesQueryHandler(_repoMock.Object);

        // Act
        await handler.Handle(
            new GetRawMaterialRatesQuery { ProductId = productId },
            CancellationToken.None);

        // Assert — only the specified product, nothing else
        _repoMock.Verify(r => r.GetByProductIdAsync(productId, It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.GetByProductIdAsync(
            It.Is<Guid>(id => id != productId), It.IsAny<CancellationToken>()), Times.Never);
    }
}
