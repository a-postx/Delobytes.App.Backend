using System.Text.Json;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

public class OzonAuthHandler : DelegatingHandler
{
    /// <summary>
    /// Request option carrying the specific Connection to authenticate against. Set by
    /// <see cref="OzonApiClient"/> before sending a request so this handler never has to guess
    /// which of a tenant's (possibly several) Ozon connections to use.
    /// </summary>
    public static readonly HttpRequestOptionsKey<Guid> ConnectionIdOptionKey = new HttpRequestOptionsKey<Guid>(
        "Delobytes.Integrations.Ozon.ConnectionId");

    private readonly IConnectionResolver _connectionResolver;
    private readonly ILogger<OzonAuthHandler> _logger;

    public OzonAuthHandler(IConnectionResolver connectionResolver, ILogger<OzonAuthHandler> logger)
    {
        _connectionResolver = connectionResolver;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        Connection connection;

        if (request.Options.TryGetValue(ConnectionIdOptionKey, out Guid connectionId))
        {
            connection = await _connectionResolver.ResolveByIdAsync(connectionId, ct);
        }
        else
        {
            // Temporary compatibility path for callers not yet migrated to pass ConnectionId;
            // picks an arbitrary active Ozon connection, which is wrong for tenants with more
            // than one. Remove once every caller sets ConnectionIdOptionKey.
            _logger.LogWarning(
                "OzonAuthHandler: request has no ConnectionId option, falling back to " +
                "FirstOrDefault resolution for channel 'ozon'.");

            connection = await _connectionResolver.ResolveAsync("ozon", ct);
        }

        string sellerId = ExtractSellerId(connection);

        request.Headers.TryAddWithoutValidation("Client-Id", sellerId);
        request.Headers.TryAddWithoutValidation("Api-Key", connection.ApiKey);

        return await base.SendAsync(request, ct);
    }

    private static string ExtractSellerId(Connection connection)
    {
        if (string.IsNullOrEmpty(connection.Settings))
        {
            throw new InvalidOperationException(
                $"Connection '{connection.Id}' has no Settings; sellerId is required for Ozon.");
        }

        using JsonDocument doc = JsonDocument.Parse(connection.Settings);

        if (!doc.RootElement.TryGetProperty("sellerId", out JsonElement sellerIdElement))
        {
            throw new InvalidOperationException(
                $"Connection '{connection.Id}' Settings does not contain 'sellerId'.");
        }

        string? sellerId = sellerIdElement.GetString();

        if (string.IsNullOrEmpty(sellerId))
        {
            throw new InvalidOperationException(
                $"Connection '{connection.Id}' sellerId is empty.");
        }

        return sellerId;
    }
}
