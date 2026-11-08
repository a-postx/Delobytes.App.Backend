using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// A channel client that must be told which system channel template it serves before its first
/// call. The factory applies the binding; clients without this capability need none.
/// </summary>
public interface IChannelApiClientTemplateBinder
{
    public void SetTemplate(SystemChannelTemplate template);
}
