using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Factory for creating channel-specific API clients.
/// </summary>
public interface IChannelApiClientFactory
{
    /// <summary>
    /// Creates an API client for the specified channel.
    /// </summary>
    /// <param name="channelCode">The channel code (e.g., "wildberries", "ozon").</param>
    /// <returns>An instance of <see cref="IChannelApiClient"/>.</returns>
    public IChannelApiClient Create(string channelCode);
}
