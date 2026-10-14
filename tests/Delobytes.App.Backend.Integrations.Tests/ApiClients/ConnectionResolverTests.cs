using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

public class ConnectionResolverTests
{
    private readonly Mock<IConnectionRepository> _connectionRepo = new();
    private readonly Mock<ITenantContext> _tenantContext = new();

    private ConnectionResolver CreateResolver()
    {
        return new ConnectionResolver(_connectionRepo.Object, _tenantContext.Object);
    }

    private static Connection BuildConnection(Guid id, bool isActive = true)
    {
        return new Connection
        {
            Id = id,
            ChannelId = Guid.NewGuid(),
            Name = "Wildberries",
            ApiKey = "some-key",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public async Task ResolveByIdAsync_ActiveConnectionExists_ReturnsIt()
    {
        Guid connectionId = Guid.NewGuid();
        Connection connection = BuildConnection(connectionId);

        _connectionRepo
            .Setup(r => r.FindByIdAsync(connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        ConnectionResolver resolver = CreateResolver();

        Connection result = await resolver.ResolveByIdAsync(connectionId, CancellationToken.None);

        result.Should().BeSameAs(connection);
    }

    [Fact]
    public async Task ResolveByIdAsync_ConnectionNotFound_ThrowsInvalidOperationException()
    {
        Guid connectionId = Guid.NewGuid();

        _connectionRepo
            .Setup(r => r.FindByIdAsync(connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        ConnectionResolver resolver = CreateResolver();

        Func<Task> act = () => resolver.ResolveByIdAsync(connectionId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{connectionId}*");
    }

    [Fact]
    public async Task ResolveByIdAsync_ConnectionInactive_ThrowsInvalidOperationException()
    {
        Guid connectionId = Guid.NewGuid();
        Connection connection = BuildConnection(connectionId, isActive: false);

        _connectionRepo
            .Setup(r => r.FindByIdAsync(connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        ConnectionResolver resolver = CreateResolver();

        Func<Task> act = () => resolver.ResolveByIdAsync(connectionId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{connectionId}*");
    }

    [Fact]
    public async Task ResolveAsync_StillWorksByChannelCode_ForBackwardCompatibility()
    {
        Connection connection = BuildConnection(Guid.NewGuid());
        connection.SystemChannelTemplate = new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "wildberries",
            DisplayName = "Wildberries",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _connectionRepo
            .Setup(r => r.GetAllByTenantAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { connection });

        ConnectionResolver resolver = CreateResolver();

        Connection result = await resolver.ResolveAsync("wildberries", CancellationToken.None);

        result.Should().BeSameAs(connection);
    }
}
