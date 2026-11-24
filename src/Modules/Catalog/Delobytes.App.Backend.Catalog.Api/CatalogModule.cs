using System.Reflection;
using Delobytes.App.Backend.Catalog.Application.Commands.Products;
using Delobytes.App.Backend.Catalog.Infrastructure;
using Delobytes.App.Backend.Contracts.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Delobytes.App.Backend.Catalog.Api;

/// <summary>
/// Registers the Catalog module with the host.
/// </summary>
public sealed class CatalogModule : IModule
{
    /// <inheritdoc/>
    public string Name => "Catalog";

    /// <inheritdoc/>
    public Assembly HandlerAssembly => typeof(UpdateProductCommand).Assembly;

    /// <inheritdoc/>
    public void AddApi(IServiceCollection services) => services.AddCatalogApi();

    /// <inheritdoc/>
    public void AddInfrastructure(IServiceCollection services, ModuleContext context) =>
        services.AddCatalogInfrastructure(context.Secrets.ConnectionString);
}
