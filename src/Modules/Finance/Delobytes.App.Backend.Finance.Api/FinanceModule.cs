using System.Reflection;
using Delobytes.App.Backend.Contracts.Modules;
using Delobytes.App.Backend.Finance.Application.Queries;
using Delobytes.App.Backend.Finance.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Delobytes.App.Backend.Finance.Api;

/// <summary>
/// Registers the Finance module with the host.
/// </summary>
public sealed class FinanceModule : IModule
{
    /// <inheritdoc/>
    public string Name => "Finance";

    /// <inheritdoc/>
    public Assembly HandlerAssembly => typeof(GetPnlLinesQuery).Assembly;

    /// <inheritdoc/>
    public void AddApi(IServiceCollection services) => services.AddFinanceApi();

    /// <inheritdoc/>
    public void AddInfrastructure(IServiceCollection services, ModuleContext context) =>
        services.AddFinanceInfrastructure(context.Secrets.ConnectionString);
}
