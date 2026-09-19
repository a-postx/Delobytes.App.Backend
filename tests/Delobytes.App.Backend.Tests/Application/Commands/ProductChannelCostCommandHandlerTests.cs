using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.ProductChannelCosts.DeleteProductChannelCost;
using Delobytes.App.Backend.Catalog.Application.Commands.ProductChannelCosts.UpsertProductChannelCost;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.ProductChannelCosts.GetProductChannelCosts;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

public class ProductChannelCostCommandHandlerTests
{
    private readonly Mock<IProductChannelCostRepository> _repoMock = new();

    private static ProductChannelCost BuildCost(
        Guid? id = null,
        Guid? productId = null,
        Guid? channelId = null,
        Guid? costTypeId = null,
        decimal amount = 150m)
        => new ProductChannelCost
        {
            Id = id ?? Guid.NewGuid(),
            ProductId = productId ?? Guid.NewGuid(),
            ChannelId = channelId ?? Guid.NewGuid(),
            CostTypeId = costTypeId ?? Guid.NewGuid(),
            Amount = amount,
            CostType = new CostType
            {
                Id = costTypeId ?? Guid.NewGuid(),
                Name = "Логистика",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // ── Upsert — create path ───────────────────────────────────────────────

    [Fact]
    public async Task UpsertProductChannelCost_NoExistingMatch_CreatesNewRecord()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();
        Guid costTypeId = Guid.NewGuid();

        _repoMock
            .Setup(r => r.GetByProductAndChannelAsync(productId, channelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductChannelCost>());

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpsertProductChannelCostCommandHandler handler =
            new UpsertProductChannelCostCommandHandler(_repoMock.Object);

        UpsertProductChannelCostCommand command = new UpsertProductChannelCostCommand
        {
            ProductId = productId,
            ChannelId = channelId,
            CostTypeId = costTypeId,
            Amount = 250m,
        };

        // Act
        UpsertProductChannelCostResponse response =
            await handler.Handle(command, CancellationToken.None);

        // Assert
        response.Id.Should().NotBe(Guid.Empty);
        response.Created.Should().BeTrue();

        _repoMock.Verify(
            r => r.Add(It.Is<ProductChannelCost>(c =>
                c.ProductId == productId &&
                c.ChannelId == channelId &&
                c.CostTypeId == costTypeId &&
                c.Amount == 250m)),
            Times.Once);

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertProductChannelCost_MatchingCostTypeExists_UpdatesAmountAndReturnsCreatedFalse()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();
        Guid costTypeId = Guid.NewGuid();

        ProductChannelCost existing = BuildCost(
            productId: productId,
            channelId: channelId,
            costTypeId: costTypeId,
            amount: 100m);

        _repoMock
            .Setup(r => r.GetByProductAndChannelAsync(productId, channelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductChannelCost> { existing });

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpsertProductChannelCostCommandHandler handler =
            new UpsertProductChannelCostCommandHandler(_repoMock.Object);

        // Act
        UpsertProductChannelCostResponse response = await handler.Handle(
            new UpsertProductChannelCostCommand
            {
                ProductId = productId,
                ChannelId = channelId,
                CostTypeId = costTypeId,
                Amount = 350m,
            },
            CancellationToken.None);

        // Assert
        response.Id.Should().Be(existing.Id);
        response.Created.Should().BeFalse();
        existing.Amount.Should().Be(350m);
        existing.UpdatedAt.Should().NotBeNull();

        _repoMock.Verify(r => r.Add(It.IsAny<ProductChannelCost>()), Times.Never);
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertProductChannelCost_DifferentCostTypeInChannel_CreatesNewWithoutTouchingExisting()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();
        Guid existingCostTypeId = Guid.NewGuid();
        Guid newCostTypeId = Guid.NewGuid();

        ProductChannelCost existing = BuildCost(
            productId: productId,
            channelId: channelId,
            costTypeId: existingCostTypeId,
            amount: 100m);

        _repoMock
            .Setup(r => r.GetByProductAndChannelAsync(productId, channelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductChannelCost> { existing });

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpsertProductChannelCostCommandHandler handler =
            new UpsertProductChannelCostCommandHandler(_repoMock.Object);

        // Act
        UpsertProductChannelCostResponse response = await handler.Handle(
            new UpsertProductChannelCostCommand
            {
                ProductId = productId,
                ChannelId = channelId,
                CostTypeId = newCostTypeId,
                Amount = 80m,
            },
            CancellationToken.None);

        // Assert
        response.Created.Should().BeTrue();
        existing.Amount.Should().Be(100m);

        _repoMock.Verify(
            r => r.Add(It.Is<ProductChannelCost>(c => c.CostTypeId == newCostTypeId && c.Amount == 80m)),
            Times.Once);
    }

    [Fact]
    public async Task UpsertProductChannelCost_ZeroAmount_IsAccepted()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByProductAndChannelAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductChannelCost>());

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        UpsertProductChannelCostCommandHandler handler =
            new UpsertProductChannelCostCommandHandler(_repoMock.Object);

        // Act
        UpsertProductChannelCostResponse response = await handler.Handle(
            new UpsertProductChannelCostCommand
            {
                ProductId = Guid.NewGuid(),
                ChannelId = Guid.NewGuid(),
                CostTypeId = Guid.NewGuid(),
                Amount = 0m,
            },
            CancellationToken.None);

        // Assert — zero is valid (buyer pays delivery in own shop channel)
        response.Created.Should().BeTrue();
        _repoMock.Verify(
            r => r.Add(It.Is<ProductChannelCost>(c => c.Amount == 0m)),
            Times.Once);
    }

    // ── Delete ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteProductChannelCost_ExistingId_RemovesAndReturnsFound()
    {
        // Arrange
        ProductChannelCost existing = BuildCost();

        _repoMock
            .Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        DeleteProductChannelCostCommandHandler handler =
            new DeleteProductChannelCostCommandHandler(_repoMock.Object);

        // Act
        DeleteProductChannelCostResponse response = await handler.Handle(
            new DeleteProductChannelCostCommand { Id = existing.Id },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeTrue();
        _repoMock.Verify(r => r.Remove(existing), Times.Once);
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteProductChannelCost_NotFound_ReturnsFalseWithoutSaving()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductChannelCost?)null);

        DeleteProductChannelCostCommandHandler handler =
            new DeleteProductChannelCostCommandHandler(_repoMock.Object);

        // Act
        DeleteProductChannelCostResponse response = await handler.Handle(
            new DeleteProductChannelCostCommand { Id = Guid.NewGuid() },
            CancellationToken.None);

        // Assert
        response.Found.Should().BeFalse();
        _repoMock.Verify(r => r.Remove(It.IsAny<ProductChannelCost>()), Times.Never);
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── GetByProduct query ────────────────────────────────────────────────

    [Fact]
    public async Task GetProductChannelCosts_ByProductOnly_ReturnsMappedList()
    {
        // Arrange
        Guid productId = Guid.NewGuid();

        List<ProductChannelCost> costs = new List<ProductChannelCost>
        {
            BuildCost(productId: productId, amount: 100m),
            BuildCost(productId: productId, amount: 200m),
        };

        _repoMock
            .Setup(r => r.GetByProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(costs);

        GetProductChannelCostsQueryHandler handler =
            new GetProductChannelCostsQueryHandler(_repoMock.Object);

        // Act
        GetProductChannelCostsResponse response = await handler.Handle(
            new GetProductChannelCostsQuery { ProductId = productId },
            CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(2);
        response.Items.Should().OnlyContain(i => i.ProductId == productId);
        _repoMock.Verify(r => r.GetByProductAsync(productId, It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.GetByProductAndChannelAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetProductChannelCosts_ByProductAndChannel_UsesChannelFilter()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();

        List<ProductChannelCost> costs = new List<ProductChannelCost>
        {
            BuildCost(productId: productId, channelId: channelId, amount: 150m),
        };

        _repoMock
            .Setup(r => r.GetByProductAndChannelAsync(productId, channelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(costs);

        GetProductChannelCostsQueryHandler handler =
            new GetProductChannelCostsQueryHandler(_repoMock.Object);

        // Act
        GetProductChannelCostsResponse response = await handler.Handle(
            new GetProductChannelCostsQuery { ProductId = productId, ChannelId = channelId },
            CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(1);
        _repoMock.Verify(r => r.GetByProductAndChannelAsync(productId, channelId, It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.GetByProductAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetProductChannelCosts_MapsAllFields()
    {
        // Arrange
        Guid productId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();
        Guid costTypeId = Guid.NewGuid();

        ProductChannelCost cost = new ProductChannelCost
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ChannelId = channelId,
            CostTypeId = costTypeId,
            Amount = 300m,
            CostType = new CostType
            {
                Id = costTypeId,
                Name = "Фулфилмент",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        _repoMock
            .Setup(r => r.GetByProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductChannelCost> { cost });

        GetProductChannelCostsQueryHandler handler =
            new GetProductChannelCostsQueryHandler(_repoMock.Object);

        // Act
        GetProductChannelCostsResponse response = await handler.Handle(
            new GetProductChannelCostsQuery { ProductId = productId },
            CancellationToken.None);

        // Assert
        ProductChannelCostItem item = response.Items.Single();
        item.Id.Should().Be(cost.Id);
        item.ProductId.Should().Be(productId);
        item.ChannelId.Should().Be(channelId);
        item.CostTypeId.Should().Be(costTypeId);
        item.CostTypeName.Should().Be("Фулфилмент");
        item.Amount.Should().Be(300m);
        item.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetProductChannelCosts_EmptyResult_ReturnsEmptyList()
    {
        // Arrange
        Guid productId = Guid.NewGuid();

        _repoMock
            .Setup(r => r.GetByProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductChannelCost>());

        GetProductChannelCostsQueryHandler handler =
            new GetProductChannelCostsQueryHandler(_repoMock.Object);

        // Act
        GetProductChannelCostsResponse response = await handler.Handle(
            new GetProductChannelCostsQuery { ProductId = productId },
            CancellationToken.None);

        // Assert
        response.Items.Should().BeEmpty();
    }
}
