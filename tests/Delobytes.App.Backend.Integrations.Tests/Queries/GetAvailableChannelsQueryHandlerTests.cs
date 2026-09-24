using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.DTOs.Channels;
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

    private GetAvailableChannelsQueryHandler CreateHandler()
    {
        return new GetAvailableChannelsQueryHandler(_templateRepo.Object);
    }

    [Fact]
    public async Task Handle_NoTemplates_ReturnsEmptyList()
    {
        _templateRepo
            .Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SystemChannelTemplate>());

        GetAvailableChannelsQueryHandler handler = CreateHandler();
        GetAvailableChannelsResponse result =
            await handler.Handle(new GetAvailableChannelsQuery(), CancellationToken.None);

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithTemplates_MapsAllFieldsCorrectly()
    {
        SystemChannelTemplate wb = BuildTemplate("wildberries", "Wildberries");
        SystemChannelTemplate ozon = BuildTemplate("ozon", "Ozon");

        _templateRepo
            .Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SystemChannelTemplate> { wb, ozon });

        GetAvailableChannelsQueryHandler handler = CreateHandler();
        GetAvailableChannelsResponse result =
            await handler.Handle(new GetAvailableChannelsQuery(), CancellationToken.None);

        result.Items.Should().HaveCount(2);

        AvailableChannelDto wbDto = result.Items.Single(i => i.Code == "wildberries");
        wbDto.Id.Should().Be(wb.Id);
        wbDto.Code.Should().Be("wildberries");
        wbDto.DisplayName.Should().Be("Wildberries");
        wbDto.Description.Should().Be("Description for Wildberries");
        wbDto.ApiVersion.Should().Be("v1");
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

        GetAvailableChannelsQueryHandler handler = CreateHandler();
        GetAvailableChannelsResponse result =
            await handler.Handle(new GetAvailableChannelsQuery(), CancellationToken.None);

        AvailableChannelDto dto = result.Items.Single();
        dto.Id.Should().Be(template.Id);
        dto.Code.Should().Be("wildberries");
        dto.DisplayName.Should().Be("Wildberries");
        dto.Description.Should().Be("Крупнейший маркетплейс");
        dto.ApiVersion.Should().Be("v3");
    }
}
