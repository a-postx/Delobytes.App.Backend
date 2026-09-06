using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// Factory for creating channel-specific API clients.
/// </summary>
public class ChannelApiClientFactory : IChannelApiClientFactory
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelApiClientFactory"/> class.
    /// </summary>
    /// <param name="serviceProvider">Service provider.</param>
    public ChannelApiClientFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    public IChannelApiClient Create(string channelCode, Connection connection)
    {
        return channelCode.ToLowerInvariant() switch
        {
            "wildberries" => _serviceProvider.GetRequiredService<WildberriesApiClient>(),
            _ => throw new NotSupportedException($"Channel '{channelCode}' is not supported.")
        };
    }
}
