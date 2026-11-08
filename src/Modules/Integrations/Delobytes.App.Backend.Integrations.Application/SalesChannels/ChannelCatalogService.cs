using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.SalesChannels;

/// <summary>
/// Injectable view of <see cref="ChannelCatalog"/>, for consumers that take dependencies rather
/// than call a static.
/// </summary>
public interface IChannelCatalogService
{
    public IReadOnlyList<ChannelDescriptor> All { get; }

    public bool TryGet(string? code, out ChannelDescriptor descriptor);
}

public sealed class ChannelCatalogService : IChannelCatalogService
{
    public IReadOnlyList<ChannelDescriptor> All
    {
        get
        {
            return ChannelCatalog.All;
        }
    }

    public bool TryGet(string? code, out ChannelDescriptor descriptor)
    {
        return ChannelCatalog.TryGet(code, out descriptor);
    }
}
