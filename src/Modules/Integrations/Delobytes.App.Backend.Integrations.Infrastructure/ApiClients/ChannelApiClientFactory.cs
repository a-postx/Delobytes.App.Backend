using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// Factory for creating channel-specific API clients. Each channel registers its client under its
/// own key, so adding a channel needs no edit here.
/// </summary>
public class ChannelApiClientFactory : IChannelApiClientFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ChannelApiClientFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    public IChannelApiClient Create(SystemChannelTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        string code = template.Code.ToLowerInvariant();
        IChannelApiClient? client = _serviceProvider.GetKeyedService<IChannelApiClient>(code);

        if (client == null)
        {
            throw new NotSupportedException($"Channel '{template.Code}' is not supported.");
        }

        // A client that resolves its endpoints from the template is bound here, at the one place
        // that hands clients out, so callers never configure it themselves.
        if (client is IChannelApiClientTemplateBinder binder)
        {
            binder.SetTemplate(template);
        }

        return client;
    }
}
