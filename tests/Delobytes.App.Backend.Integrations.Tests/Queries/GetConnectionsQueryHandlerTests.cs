using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.DTOs.Connections;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Queries.GetConnections;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Queries;

public class GetConnectionsQueryHandlerTests
{
    private readonly Mock<IConnectionRepository> _connectionRepo = new();

    private GetConnectionsQueryHandler CreateHandler()
    {
        return new GetConnectionsQueryHandler(_connectionRepo.Object);
    }

    [Fact]
    public async Task Handle_NoConnections_ReturnsEmptyList()
    {
        _connectionRepo
            .Setup(r => r.GetAllByTenantAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection>());

        GetConnectionsQueryHandler handler = CreateHandler();
        GetConnectionsResponse result =
            await handler.Handle(new GetConnectionsQuery(), CancellationToken.None);

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithConnections_MapsAllFieldsCorrectly()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset lastSync = now.AddHours(-2);
        Guid channelId = Guid.NewGuid();

        SystemChannelTemplate template = new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "wildberries",
            DisplayName = "Wildberries",
            ApiBaseUrl = "https://example.com",
            ApiVersion = "v3",
            IsActive = true,
            CreatedAt = now,
        };

        Connection connection = new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            SystemChannelTemplateId = template.Id,
            Name = "Wildberries",
            ApiKey = "some-key-1234567890",
            IsActive = true,
            LastSyncAt = lastSync,
            CreatedAt = now,
            SystemChannelTemplate = template,
            CustomerName = "My Store",
            CustomerLegalName = "LLC MyCompany",
            CustomerInn = "1234567890",
        };

        _connectionRepo
            .Setup(r => r.GetAllByTenantAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { connection });

        GetConnectionsQueryHandler handler = CreateHandler();
        GetConnectionsResponse result =
            await handler.Handle(new GetConnectionsQuery(), CancellationToken.None);

        result.Items.Should().HaveCount(1);
        ConnectionDto dto = result.Items.Single();

        dto.Id.Should().Be(connection.Id);
        dto.ChannelId.Should().Be(channelId);
        dto.TemplateCode.Should().Be("wildberries");
        dto.TemplateDisplayName.Should().Be("Wildberries");
        dto.IsActive.Should().BeTrue();
        dto.LastSyncAt.Should().Be(lastSync);
        dto.CreatedAt.Should().Be(now);
        dto.MaskedApiKey.Should().Be("*************567890");
        dto.CustomerName.Should().Be("My Store");
        dto.CustomerLegalName.Should().Be("LLC MyCompany");
        dto.CustomerInn.Should().Be("1234567890");
    }

    [Fact]
    public async Task Handle_ConnectionWithNullTemplate_UsesEmptyStrings()
    {
        Connection connection = new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            SystemChannelTemplateId = Guid.NewGuid(),
            Name = "Unknown",
            ApiKey = "some-key",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            SystemChannelTemplate = null!,
        };

        _connectionRepo
            .Setup(r => r.GetAllByTenantAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { connection });

        GetConnectionsQueryHandler handler = CreateHandler();
        GetConnectionsResponse result =
            await handler.Handle(new GetConnectionsQuery(), CancellationToken.None);

        ConnectionDto dto = result.Items.Single();
        dto.TemplateCode.Should().BeEmpty();
        dto.TemplateDisplayName.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MultipleConnections_ReturnsAll()
    {
        List<Connection> connections = new List<Connection>
        {
            new Connection
            {
                Id = Guid.NewGuid(), ChannelId = Guid.NewGuid(),
                SystemChannelTemplateId = Guid.NewGuid(),
                Name = "WB",
                ApiKey = "k1", IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
                SystemChannelTemplate = new SystemChannelTemplate { Code = "wildberries", DisplayName = "Wildberries",
                    ApiBaseUrl = "x", ApiVersion = "v3" },
            },
            new Connection
            {
                Id = Guid.NewGuid(), ChannelId = Guid.NewGuid(),
                SystemChannelTemplateId = Guid.NewGuid(),
                Name = "Ozon",
                ApiKey = "k2", IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
                SystemChannelTemplate = new SystemChannelTemplate { Code = "ozon", DisplayName = "Ozon",
                    ApiBaseUrl = "x", ApiVersion = "v1" },
            },
        };

        _connectionRepo
            .Setup(r => r.GetAllByTenantAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(connections);

        GetConnectionsQueryHandler handler = CreateHandler();
        GetConnectionsResponse result =
            await handler.Handle(new GetConnectionsQuery(), CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.Items.Select(i => i.TemplateCode).Should().Contain("wildberries", "ozon");
    }

    [Fact]
    public async Task Handle_ShortApiKey_MaskedApiKeyIsNull()
    {
        Connection connection = new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            SystemChannelTemplateId = Guid.NewGuid(),
            Name = "Test",
            ApiKey = "short",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            SystemChannelTemplate = new SystemChannelTemplate { Code = "test", DisplayName = "Test",
                ApiBaseUrl = "x", ApiVersion = "v1" },
        };

        _connectionRepo
            .Setup(r => r.GetAllByTenantAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { connection });

        GetConnectionsQueryHandler handler = CreateHandler();
        GetConnectionsResponse result =
            await handler.Handle(new GetConnectionsQuery(), CancellationToken.None);

        result.Items.Single().MaskedApiKey.Should().BeNull();
    }
}
