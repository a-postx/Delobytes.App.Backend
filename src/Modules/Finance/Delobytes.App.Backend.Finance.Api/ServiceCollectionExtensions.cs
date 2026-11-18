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
}
