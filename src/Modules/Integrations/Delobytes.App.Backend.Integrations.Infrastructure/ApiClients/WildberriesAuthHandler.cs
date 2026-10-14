using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

public class WildberriesAuthHandler : DelegatingHandler
{
    /// <summary>
    /// Request option carrying the specific Connection to authenticate against. Set by
    /// <see cref="WildberriesApiClient"/> before sending a request so this handler never has to
    /// guess which of a tenant's (possibly several) Wildberries connections to use.
    /// </summary>
    public static readonly HttpRequestOptionsKey<Guid> ConnectionIdOptionKey = new HttpRequestOptionsKey<Guid>(
        "Delobytes.Integrations.Wildberries.ConnectionId");

    private readonly IConnectionResolver _connectionResolver;
    private readonly ILogger<WildberriesAuthHandler> _logger;

    public WildberriesAuthHandler(IConnectionResolver connectionResolver, ILogger<WildberriesAuthHandler> logger)
    {
        _connectionResolver = connectionResolver;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        // GetAccountInfoAsync authenticates a not-yet-persisted connection with a raw API key
        // supplied by the caller and sets this header itself; resolving a Connection from the
        // database here would silently replace that key with an unrelated existing connection's.
        if (request.Headers.Authorization is not null || request.Headers.Contains("Authorization"))
        {
            return await base.SendAsync(request, ct);
        }

        Connection connection;

        if (request.Options.TryGetValue(ConnectionIdOptionKey, out Guid connectionId))
        {
            connection = await _connectionResolver.ResolveByIdAsync(connectionId, ct);
        }
        else
        {
            // Temporary compatibility path for callers not yet migrated to pass ConnectionId;
            // picks an arbitrary active Wildberries connection, which is wrong for tenants with
            // more than one. Remove once every caller sets ConnectionIdOptionKey.
            _logger.LogWarning(
                "WildberriesAuthHandler: request has no ConnectionId option, falling back to " +
                "FirstOrDefault resolution for channel 'wildberries'.");

            connection = await _connectionResolver.ResolveAsync("wildberries", ct);
        }

        request.Headers.TryAddWithoutValidation("Authorization", connection.ApiKey);

        return await base.SendAsync(request, ct);
    }
}
