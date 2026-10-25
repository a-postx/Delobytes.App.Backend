using Delobytes.App.Backend.Integrations.Application.Queries.GetAvailableChannels;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Delobytes.App.Backend.Integrations.Api;

/// <summary>
/// Registers Integrations's presentation-layer services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Integrations Api.
    /// </summary>
    public static IServiceCollection AddIntegrationsApi(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(GetAvailableChannelsQuery).Assembly);

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
    /// Registers Integrations's MediatR command/query handlers.
    /// </summary>
    public static MediatRServiceConfiguration AddIntegrationsHandlers(this MediatRServiceConfiguration configuration)
    {
        configuration.RegisterServicesFromAssembly(typeof(GetAvailableChannelsQuery).Assembly);
        return configuration;
    }
}
