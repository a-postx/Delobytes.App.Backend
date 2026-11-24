using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Delobytes.App.Backend.Contracts.Modules;

/// <summary>
/// A bounded context registered with the host. Implementations are discovered by scanning the
/// module <c>*.Api</c> assemblies, so adding a module does not require editing the host composition.
/// </summary>
public interface IModule
{
    /// <summary>
    /// Stable module name used for logging and deterministic registration order.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Assembly that contains the module handlers, validators and requests.
    /// </summary>
    public Assembly HandlerAssembly { get; }

    /// <summary>
    /// Registers the module presentation layer: controllers, request validators and Swagger comments.
    /// </summary>
    /// <param name="services">Service collection.</param>
    public void AddApi(IServiceCollection services);

    /// <summary>
    /// Registers the module persistence, messaging and integration services.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="context">Configuration and secrets supplied by the host.</param>
    public void AddInfrastructure(IServiceCollection services, ModuleContext context);
}
