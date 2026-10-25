using Delobytes.App.Backend.Catalog.Application.Commands.Products;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Delobytes.App.Backend.Catalog.Api;

/// <summary>
/// Registers Catalog's presentation-layer services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Catalog Api.
    /// </summary>
    public static IServiceCollection AddCatalogApi(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(UpdateProductCommand).Assembly);

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
    /// Registers Catalog's MediatR command/query handlers.
    /// </summary>
    public static MediatRServiceConfiguration AddCatalogHandlers(this MediatRServiceConfiguration configuration)
    {
        configuration.RegisterServicesFromAssembly(typeof(UpdateProductCommand).Assembly);
        return configuration;
    }
}
