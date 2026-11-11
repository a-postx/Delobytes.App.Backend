using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Application.SalesChannels;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Delobytes.App.Backend.Integrations.Infrastructure.SalesChannels;

/// <summary>
/// One marketplace's registration. Adding a channel means adding a module and one
/// <c>AddChannelModule</c> call; no switch or if statement about channel codes changes.
/// </summary>
public abstract class ChannelModule
{
    /// <summary>
    /// Gets the channel's description: code, name, supported import kinds and scheduling policy.
    /// </summary>
    public abstract ChannelDescriptor Descriptor { get; }

    /// <summary>
    /// Gets the concrete job types this module serves, in scheduling order, each tagged with the
    /// import kind it belongs to. How a kind is split into job types — for example one orders job
    /// per posting schema — is this module's own decision.
    /// </summary>
    public abstract IReadOnlyList<(JobType JobType, ChannelJobKind Kind)> JobTypes { get; }

    public abstract void RegisterProducts(IServiceCollection services);

    public abstract void RegisterOrders(IServiceCollection services);

    public abstract void RegisterApiClient(IServiceCollection services);

    public abstract void RegisterApiKeyValidator(IServiceCollection services);

    /// <summary>
    /// Returns the rate-limit method the channel paces the given job type on, or <c>null</c> when
    /// the channel has no endpoint-level limiter for it.
    /// </summary>
    public virtual string? GetRateLimitMethod(JobType jobType)
    {
        return null;
    }
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

        module.RegisterProducts(services);
        module.RegisterOrders(services);
        module.RegisterApiClient(services);
        module.RegisterApiKeyValidator(services);

        services.AddSingleton<ChannelModule>(module);

        return services;
    }
}
