using Delobytes.App.Backend.Integrations.Application.Interfaces;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

public class ApiKeyValidatorFactory : IApiKeyValidatorFactory
{
    private readonly WildberriesApiKeyValidator _wildberries;
    private readonly OzonApiKeyValidator _ozon;

    public ApiKeyValidatorFactory(
        WildberriesApiKeyValidator wildberries,
        OzonApiKeyValidator ozon)
    {
        _wildberries = wildberries;
        _ozon = ozon;
    }

    public IApiKeyValidator Create(string channelCode)
    {
        return channelCode.ToLowerInvariant() switch
        {
            "wildberries" => _wildberries,
            "ozon" => _ozon,
            _ => throw new NotSupportedException($"Валидатор для канала '{channelCode}' не найден."),
        };
    }
}
