using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

public class YandexKitAuthHandler : DelegatingHandler
{
    private readonly IConnectionResolver _connectionResolver;

    public YandexKitAuthHandler(IConnectionResolver connectionResolver)
    {
        _connectionResolver = connectionResolver;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        Connection connection = await _connectionResolver.ResolveAsync("yandex.kit", ct);

        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {connection.ApiKey}");

        return await base.SendAsync(request, ct);
    }
}
