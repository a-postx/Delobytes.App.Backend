using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Application.Commands.Channels.CreateChannel;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Channels.GetChannels;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Catalog;

/// <summary>
/// Tests for Channel.Code: it must be persisted on creation and surfaced by GetChannels,
/// so the frontend can render a marketplace badge (prefix + nmID) without guessing the
/// channel type from its display name.
/// </summary>
public class ChannelCodeTests
{
    private readonly Mock<IChannelRepository> _repoMock = new();

    // ── CreateChannelCommandHandler ──────────────────────────────────────

    [Fact]
    public async Task CreateChannel_WithSystemTemplateCode_PersistsCode()
    {
        // Arrange
        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateChannelCommandHandler handler = new CreateChannelCommandHandler(_repoMock.Object);

        Guid templateId = Guid.NewGuid();

        // Act
        CreateChannelResponse response = await handler.Handle(
            new CreateChannelCommand
            {
                Name = "Wildberries",
                SystemChannelTemplateId = templateId,
                Code = "wildberries",
            },
            CancellationToken.None);

        // Assert
        response.Id.Should().NotBe(Guid.Empty);

        _repoMock.Verify(
            r => r.Add(It.Is<Channel>(c =>
                c.Name == "Wildberries" &&
                c.SystemChannelTemplateId == templateId &&
                c.Code == "wildberries" &&
                c.IsCustom == false)),
            Times.Once);

        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateChannel_CustomChannel_LeavesCodeNull()
    {
        // Arrange: a custom channel has no system template, so there is no code to copy.
        _repoMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        CreateChannelCommandHandler handler = new CreateChannelCommandHandler(_repoMock.Object);

        // Act
        await handler.Handle(
            new CreateChannelCommand
            {
                Name = "My own shop",
                SystemChannelTemplateId = null,
                Code = null,
                CustomApiUrl = "https://shop.example.com/api",
            },
            CancellationToken.None);

        // Assert
        _repoMock.Verify(
            r => r.Add(It.Is<Channel>(c =>
                c.Code == null &&
                c.IsCustom == true)),
            Times.Once);
    }

    // ── GetChannelsQueryHandler ──────────────────────────────────────────

    [Fact]
    public async Task GetChannels_ReturnsCodeForEachChannel()
    {
        // Arrange
        List<Channel> channels = new List<Channel>
        {
            BuildChannel("Wildberries", code: "wildberries"),
            BuildChannel("Ozon", code: "ozon"),
            BuildChannel("My own shop", code: null),
        };

        _repoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(channels);

        GetChannelsQueryHandler handler = new GetChannelsQueryHandler(_repoMock.Object);

        // Act
        GetChannelsResponse response = await handler.Handle(new GetChannelsQuery(), CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(3);
        response.Items.Should().ContainSingle(i => i.Name == "Wildberries" && i.Code == "wildberries");
        response.Items.Should().ContainSingle(i => i.Name == "Ozon" && i.Code == "ozon");
        response.Items.Should().ContainSingle(i => i.Name == "My own shop" && i.Code == null);
    }

    private static Channel BuildChannel(string name, string? code)
    {
        return new Channel
        {
            Id = Guid.NewGuid(),
            Name = name,
            Code = code,
            IsCustom = code == null,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
