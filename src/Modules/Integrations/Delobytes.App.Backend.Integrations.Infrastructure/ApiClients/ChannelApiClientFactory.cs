using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// Factory for creating channel-specific API clients.
/// </summary>
public class ChannelApiClientFactory : IChannelApiClientFactory
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelApiClientFactory"/> class.
    /// </summary>
    /// <param name="serviceProvider">Service provider.</param>
    public ChannelApiClientFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    ///
    /// <remarks>
    /// Клиент разрешается из контейнера при каждом вызове (регистрация типизированного
    /// HttpClient — transient), поэтому настройка шаблона не расшаривается между вызовами.
    /// </remarks>
    public IChannelApiClient Create(SystemChannelTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        switch (template.Code.ToLowerInvariant())
        {
            case "wildberries":
                WildberriesApiClient wildberriesClient =
                    _serviceProvider.GetRequiredService<WildberriesApiClient>();

                // WildberriesApiClient резолвит адреса эндпоинтов по шаблону,
                // поэтому клиент конфигурируется здесь, а не вызывающим кодом.
                wildberriesClient.SetTemplate(template);

                return wildberriesClient;

            case "ozon":
                return _serviceProvider.GetRequiredService<OzonApiClient>();

            case "yandex.kit":
                return _serviceProvider.GetRequiredService<YandexKitApiClient>();

            default:
                throw new NotSupportedException($"Channel '{template.Code}' is not supported.");
        }
    }
}
