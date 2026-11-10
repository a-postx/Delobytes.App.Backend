namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Identifies the pacing bucket a request belongs to: a channel, the key the channel enforces its
/// own limit against (e.g. a Wildberries API key or an Ozon Client-Id), and the API method, since
/// one channel may budget several of its methods independently.
/// </summary>
/// <param name="ChannelCode">The channel code (e.g. <c>"wildberries"</c>, <c>"ozon"</c>).</param>
/// <param name="ApiKey">The key the channel enforces its limit against.</param>
/// <param name="Method">
/// The API method. Wildberries methods are declared in
/// <see cref="Models.WildberriesApiMethodRegistry"/>, Ozon methods in
/// <see cref="Models.OrdersRateLimiterMethods"/>.
/// </param>
public readonly record struct RateLimitScope(string ChannelCode, string ApiKey, string Method);
