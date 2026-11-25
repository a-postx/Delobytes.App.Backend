using Delobytes.App.Backend.Finance.Application.Queries;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Delobytes.App.Backend.Finance.Api;

/// <summary>
/// Registers Finance's presentation-layer services.
/// </summary>
/// <remarks>
/// The module has no controllers, commands or queries yet. Only the Swagger XML comments are wired
/// so the first controller added here is documented without touching the host.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Finance Api.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddFinanceApi(this IServiceCollection services)
    {
        // Without this the request validators of the module never run: the MediatR behaviour resolves
        // them from the container and finds none.
        services.AddValidatorsFromAssembly(typeof(GetPnlLinesQuery).Assembly);

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
    /// Registers Finance&#39;s MediatR handlers.
    /// </summary>
    public static MediatRServiceConfiguration AddFinanceHandlers(this MediatRServiceConfiguration configuration)
    {
        configuration.RegisterServicesFromAssembly(typeof(GetPnlLinesQuery).Assembly);
        return configuration;
    }
}

