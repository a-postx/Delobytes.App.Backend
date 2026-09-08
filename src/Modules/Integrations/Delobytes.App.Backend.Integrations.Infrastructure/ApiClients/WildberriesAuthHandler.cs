using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

public class WildberriesAuthHandler : DelegatingHandler
{
    private readonly IConnectionResolver _connectionResolver;

    public WildberriesAuthHandler(IConnectionResolver connectionResolver)
    {
        _connectionResolver = connectionResolver;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        Connection connection = await _connectionResolver.ResolveAsync("wildberries", ct);

        request.Headers.TryAddWithoutValidation("Authorization", connection.ApiKey);

        return await base.SendAsync(request, ct);
    }
}
