using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// Factory for creating channel-specific API clients.
/// </summary>
public class ChannelApiClientFactory : IChannelApiClientFactory
{
    /// <inheritdoc/>
    public IChannelApiClient Create(string channelCode, Connection connection)
    {
        return channelCode.ToLowerInvariant() switch
        {
            "wildberries" => new WildberriesApiClient(),
            _ => throw new NotSupportedException($"Channel '{channelCode}' is not supported.")
        };
    }
}
