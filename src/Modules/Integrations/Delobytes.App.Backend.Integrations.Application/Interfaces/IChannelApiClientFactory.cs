using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Factory for creating channel-specific API clients.
/// </summary>
public interface IChannelApiClientFactory
{
    /// <summary>
    /// Creates an API client configured for the specified system channel template.
    /// </summary>
    /// <param name="template">System channel template that identifies the channel and its endpoints.</param>
    /// <returns>A ready-to-use instance of <see cref="IChannelApiClient"/>.</returns>
    public IChannelApiClient Create(SystemChannelTemplate template);
}
