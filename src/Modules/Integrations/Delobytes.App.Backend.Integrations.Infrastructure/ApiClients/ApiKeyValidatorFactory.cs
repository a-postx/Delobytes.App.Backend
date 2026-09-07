using Delobytes.App.Backend.Integrations.Application.Interfaces;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

public class ApiKeyValidatorFactory : IApiKeyValidatorFactory
{
    private readonly WildberriesApiKeyValidator _wildberries;
    private readonly OzonApiKeyValidator _ozon;
    private readonly YandexKitApiKeyValidator _yandexKit;

    public ApiKeyValidatorFactory(
        WildberriesApiKeyValidator wildberries,
        OzonApiKeyValidator ozon,
        YandexKitApiKeyValidator yandexKit)
    {
        _wildberries = wildberries;
        _ozon = ozon;
        _yandexKit = yandexKit;
    }

    public IApiKeyValidator Create(string channelCode)
    {
        return channelCode.ToLowerInvariant() switch
        {
            "wildberries" => _wildberries,
            "ozon" => _ozon,
            "yandexkit" => _yandexKit,
            _ => throw new NotSupportedException($"Валидатор для канала '{channelCode}' не найден."),
        };
    }
}
