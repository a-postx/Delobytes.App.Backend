using System.Reflection;
using Delobytes.App.Backend.Contracts.Modules;
using Delobytes.App.Backend.Sales.Application.Queries.Orders;
using Delobytes.App.Backend.Sales.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Delobytes.App.Backend.Sales.Api;

/// <summary>
/// Registers the Sales module with the host.
/// </summary>
public sealed class SalesModule : IModule
{
    /// <inheritdoc/>
    public string Name => "Sales";

    /// <inheritdoc/>
    public Assembly HandlerAssembly => typeof(GetOrdersChartQuery).Assembly;

    /// <inheritdoc/>
    public void AddApi(IServiceCollection services) => services.AddSalesApi();

    /// <inheritdoc/>
    public void AddInfrastructure(IServiceCollection services, ModuleContext context) =>
        services.AddSalesInfrastructure(context.Secrets.ConnectionString);
}
