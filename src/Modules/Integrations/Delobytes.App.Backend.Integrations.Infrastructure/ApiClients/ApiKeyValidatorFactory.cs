using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// Resolves the API-key validator a channel registered under its own key, so adding a channel
/// needs no edit here.
/// </summary>
public class ApiKeyValidatorFactory : IApiKeyValidatorFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ApiKeyValidatorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IApiKeyValidator Create(string channelCode)
    {
        IApiKeyValidator? validator = _serviceProvider.GetKeyedService<IApiKeyValidator>(
            channelCode.ToLowerInvariant());

        return validator
            ?? throw new NotSupportedException($"Валидатор для канала '{channelCode}' не найден.");
    }
}
