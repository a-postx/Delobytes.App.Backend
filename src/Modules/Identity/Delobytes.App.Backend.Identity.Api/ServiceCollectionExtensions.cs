using Delobytes.App.Backend.Identity.Api.Swagger;
using Delobytes.App.Backend.Identity.Application.Commands.CreateTenant;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Delobytes.App.Backend.Identity.Api;

/// <summary>
/// Registers Identity's presentation-layer services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Identity Api.
    /// </summary>
    public static IServiceCollection AddIdentityApi(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(CreateTenantCommand).Assembly);

        services.Configure<SwaggerGenOptions>(options =>
        {
            string xmlPath = Path.ChangeExtension(typeof(ServiceCollectionExtensions).Assembly.Location, ".xml");

            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }

            options.SchemaFilter<LoginExampleSchemaFilter>();
        });

        return services;
    }

    /// <summary>
    /// Registers Identity's MediatR command/query handlers.
    /// </summary>
    public static MediatRServiceConfiguration AddIdentityHandlers(this MediatRServiceConfiguration configuration)
    {
        configuration.RegisterServicesFromAssembly(typeof(CreateTenantCommand).Assembly);
        return configuration;
    }
}
