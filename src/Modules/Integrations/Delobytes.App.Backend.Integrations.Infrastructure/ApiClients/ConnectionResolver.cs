using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

public class ConnectionResolver : IConnectionResolver
{
    private readonly IConnectionRepository _connectionRepository;
    private readonly ITenantContext _tenantContext;

    public ConnectionResolver(
        IConnectionRepository connectionRepository,
        ITenantContext tenantContext)
    {
        _connectionRepository = connectionRepository;
        _tenantContext = tenantContext;
    }

    public async Task<Connection> ResolveAsync(string channelCode, CancellationToken ct)
    {
        IReadOnlyList<Connection> connections =
            await _connectionRepository.GetAllByTenantAsync(ct);

        Connection? connection = connections.FirstOrDefault(c =>
            c.IsActive &&
            c.Channel?.Code != null &&
            c.Channel.Code.Equals(channelCode, StringComparison.OrdinalIgnoreCase));

        if (connection is null)
        {
            throw new InvalidOperationException(
                $"Active connection for channel '{channelCode}' not found for tenant '{_tenantContext.TenantId}'.");
        }

        return connection;
    }
}
