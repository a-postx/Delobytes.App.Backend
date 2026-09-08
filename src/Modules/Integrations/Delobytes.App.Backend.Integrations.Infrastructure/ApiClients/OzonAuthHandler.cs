using System.Text.Json;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

public class OzonAuthHandler : DelegatingHandler
{
    private readonly IConnectionResolver _connectionResolver;

    public OzonAuthHandler(IConnectionResolver connectionResolver)
    {
        _connectionResolver = connectionResolver;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        Connection connection = await _connectionResolver.ResolveAsync("ozon", ct);

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
