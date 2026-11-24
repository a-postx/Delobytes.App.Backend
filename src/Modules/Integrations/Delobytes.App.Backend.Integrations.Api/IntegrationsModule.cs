using System.Reflection;
using Delobytes.App.Backend.Contracts.Modules;
using Delobytes.App.Backend.Integrations.Application.Queries;
using Delobytes.App.Backend.Integrations.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Delobytes.App.Backend.Integrations.Api;

/// <summary>
/// Registers the Integrations module with the host.
/// </summary>
public sealed class IntegrationsModule : IModule
{
    /// <inheritdoc/>
    public string Name => "Integrations";

    /// <inheritdoc/>
    public Assembly HandlerAssembly => typeof(GetAvailableChannelsQuery).Assembly;

    /// <inheritdoc/>
    public void AddApi(IServiceCollection services) => services.AddIntegrationsApi();

    /// <inheritdoc/>
    public void AddInfrastructure(IServiceCollection services, ModuleContext context) =>
        services.AddIntegrationsInfrastructure(context.Secrets.ConnectionString, context.Configuration);
}
