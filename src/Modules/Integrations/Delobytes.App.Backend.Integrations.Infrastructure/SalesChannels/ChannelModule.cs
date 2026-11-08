using Delobytes.App.Backend.Integrations.Application.SalesChannels;
using Microsoft.Extensions.DependencyInjection;

namespace Delobytes.App.Backend.Integrations.Infrastructure.SalesChannels;

/// <summary>
/// One marketplace's registration. Adding a channel means adding a module and one
/// <c>AddChannelModule</c> call; no switch or if statement about channel codes changes.
/// </summary>
public abstract class ChannelModule
{
    public abstract ChannelDescriptor Descriptor { get; }

    public abstract void RegisterOrders(IServiceCollection services);

    public abstract void RegisterApiClient(IServiceCollection services);

    public abstract void RegisterApiKeyValidator(IServiceCollection services);
}

/// <summary>
/// Channel module extensions.
/// </summary>
public static class ChannelModuleExtensions
{
    /// <summary>
    /// Adds sales channel module.
    /// </summary>
    /// <typeparam name="TModule">Module type.</typeparam>
    /// <param name="services">Service collection.</param>
    public static IServiceCollection AddChannelModule<TModule>(this IServiceCollection services)
        where TModule : ChannelModule, new()
    {
        TModule module = new TModule();

        module.RegisterOrders(services);
        module.RegisterApiClient(services);
        module.RegisterApiKeyValidator(services);

        services.AddSingleton<ChannelModule>(module);

        return services;
    }
}
