using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Channels.CreateChannel;

/// <summary>
/// Creates a sales channel (Channel) as an independent business entity.
/// A Connection (Integrations module) can be attached to it afterwards, but the channel
/// itself, its costs and its historical data exist regardless of connection status.
/// </summary>
public class CreateChannelCommand : IRequest<CreateChannelResponse>, IRequireRole
{
    public string Name { get; set; } = default!;

    /// <summary>
    /// Optional reference to a system channel template id (Wildberries, Ozon, etc.) from the
    /// Integrations module. Loose reference by design — no cross-module FK. Null means a custom channel.
    /// </summary>
    public Guid? SystemChannelTemplateId { get; set; }

    /// <summary>Custom API URL, only meaningful when SystemChannelTemplateId is null.</summary>
    public string? CustomApiUrl { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}
