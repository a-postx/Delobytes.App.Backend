using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.DTOs.Channels;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Integrations.Application.Queries.GetAvailableChannels;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Queries;

public class GetAvailableChannelsQueryHandlerTests
{
    private readonly Mock<ISystemChannelTemplateRepository> _templateRepo = new();
    private readonly Mock<IConnectionRepository> _connectionRepo = new();

    private GetAvailableChannelsQueryHandler CreateHandler()
    {
        return new GetAvailableChannelsQueryHandler(
            _templateRepo.Object,
            _connectionRepo.Object);
    }

    private static SystemChannelTemplate BuildTemplate(string code, string displayName)
    {
        return new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = code,
            DisplayName = displayName,
            ApiBaseUrl = "https://example.com",
            ApiVersion = "v1",
            Description = $"Description for {displayName}",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public async Task Handle_NoTemplates_ReturnsEmptyList()
    {
        _templateRepo
            .Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SystemChannelTemplate>());

        _connectionRepo
            .Setup(r => r.GetAllByTenantAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection>());

        GetAvailableChannelsQueryHandler handler = CreateHandler();
        GetAvailableChannelsResponse result =
            await handler.Handle(new GetAvailableChannelsQuery(), CancellationToken.None);

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NoConnections_AllTemplatesReturnIsConnectedFalse()
    {
        SystemChannelTemplate wb = BuildTemplate("wildberries", "Wildberries");
        SystemChannelTemplate ozon = BuildTemplate("ozon", "Ozon");

        _templateRepo
            .Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SystemChannelTemplate> { wb, ozon });

        _connectionRepo
            .Setup(r => r.GetAllByTenantAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection>());

        GetAvailableChannelsQueryHandler handler = CreateHandler();
        GetAvailableChannelsResponse result =
            await handler.Handle(new GetAvailableChannelsQuery(), CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.Items.Should().AllSatisfy(i => i.IsConnected.Should().BeFalse());
    }

    [Fact]
    public async Task Handle_ConnectionExists_MatchingTemplateIsConnectedTrue()
    {
        SystemChannelTemplate wb = BuildTemplate("wildberries", "Wildberries");
        SystemChannelTemplate ozon = BuildTemplate("ozon", "Ozon");

        // Подключение к Wildberries — ChannelId ссылается на Id шаблона
        Connection wbConnection = new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = wb.Id,
            Name = "Wildberries",
            ApiKey = "key",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _templateRepo
            .Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SystemChannelTemplate> { wb, ozon });

        _connectionRepo
            .Setup(r => r.GetAllByTenantAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { wbConnection });

        GetAvailableChannelsQueryHandler handler = CreateHandler();
        GetAvailableChannelsResponse result =
            await handler.Handle(new GetAvailableChannelsQuery(), CancellationToken.None);

        AvailableChannelDto wbDto = result.Items.Single(i => i.Code == "wildberries");
        AvailableChannelDto ozonDto = result.Items.Single(i => i.Code == "ozon");

        wbDto.IsConnected.Should().BeTrue();
        ozonDto.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_MapsTemplateFieldsCorrectly()
    {
        SystemChannelTemplate template = BuildTemplate("wildberries", "Wildberries");
        template.Description = "Крупнейший маркетплейс";
        template.ApiVersion = "v3";

        _templateRepo
            .Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SystemChannelTemplate> { template });

        _connectionRepo
            .Setup(r => r.GetAllByTenantAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection>());

        GetAvailableChannelsQueryHandler handler = CreateHandler();
        GetAvailableChannelsResponse result =
            await handler.Handle(new GetAvailableChannelsQuery(), CancellationToken.None);

        AvailableChannelDto dto = result.Items.Single();
        dto.Code.Should().Be("wildberries");
        dto.DisplayName.Should().Be("Wildberries");
        dto.Description.Should().Be("Крупнейший маркетплейс");
        dto.ApiVersion.Should().Be("v3");
    }
}
