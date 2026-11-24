using System.Reflection;
using Delobytes.App.Backend.Contracts.Modules;
using Delobytes.App.Backend.Identity.Application.Commands;
using Delobytes.App.Backend.Identity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Delobytes.App.Backend.Identity.Api;

/// <summary>
/// Registers the Identity module with the host.
/// </summary>
public sealed class IdentityModule : IModule
{
    /// <inheritdoc/>
    public string Name => "Identity";

    /// <inheritdoc/>
    public Assembly HandlerAssembly => typeof(CreateTenantCommand).Assembly;

    /// <inheritdoc/>
    public void AddApi(IServiceCollection services) => services.AddIdentityApi();

    /// <inheritdoc/>
    public void AddInfrastructure(IServiceCollection services, ModuleContext context)
    {
        services.AddIdentityInfrastructure(
            context.Configuration,
            context.Secrets.ConnectionString,
            context.Secrets.JwtSecretKey,
            context.Secrets.YandexClientId,
            context.Secrets.YandexClientSecret,
            context.Secrets.GoogleClientId,
            context.Secrets.GoogleClientSecret);
    }
}
