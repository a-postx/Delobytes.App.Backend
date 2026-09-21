using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Delobytes.App.Backend.Hubs;

/// <summary>
/// SignalR hub for real-time product lifecycle notifications.
/// Clients subscribe to a product group to receive deletion status changes.
/// </summary>
[Authorize]
public class ProductHub : Hub
{
    public async Task SubscribeToProduct(Guid productId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"product_{productId}");
    }

    public async Task UnsubscribeFromProduct(Guid productId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"product_{productId}");
    }
}
