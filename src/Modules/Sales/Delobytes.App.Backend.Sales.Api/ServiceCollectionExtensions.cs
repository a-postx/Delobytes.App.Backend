using Delobytes.App.Backend.Sales.Application.Queries.Orders;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Delobytes.App.Backend.Sales.Api;

/// <summary>
/// Registers Sales's presentation-layer services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Sales Api.
    /// </summary>
    public static IServiceCollection AddSalesApi(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(GetOrdersChartQuery).Assembly);

        services.Configure<SwaggerGenOptions>(options =>
        {
            string xmlPath = Path.ChangeExtension(typeof(ServiceCollectionExtensions).Assembly.Location, ".xml");

            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }
        });

        return services;
    }

    /// <summary>
    /// Registers Sales's MediatR command/query handlers.
    /// </summary>
    public static MediatRServiceConfiguration AddSalesHandlers(this MediatRServiceConfiguration configuration)
    {
        configuration.RegisterServicesFromAssembly(typeof(GetOrdersChartQuery).Assembly);
        return configuration;
    }
}
