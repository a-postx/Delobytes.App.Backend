using Delobytes.App.Backend.Options;
using Delobytes.App.Backend.Options.Validators;
using Delobytes.AspNetCore.Common.Constants;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Extensions;

internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add cross-origin resource sharing (CORS) services and configures named CORS policies. See
    /// https://docs.asp.net/en/latest/security/cors.html
    /// </summary>
    public static IServiceCollection AddCustomCors(this IServiceCollection services)
    {
        return services.AddCors(options =>
            {
                // Create named CORS policies here which you can consume using application.UseCors("PolicyName")
                // or a [EnableCors("PolicyName")] attribute on your controller or action.
                options.AddPolicy(
                    CorsPolicyNames.AllowAny,
                    x => x
                        .AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader());
            });
    }

    /// <summary>
    /// Configures the settings by binding the contents of the appsettings.json file to the specified Plain Old CLR
    /// Objects (POCO) and adding <see cref="IOptions{T}"/> objects to the services collection.
    /// </summary>
    public static IServiceCollection AddCustomOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<DelobytesBackendSecrets>, DelobytesBackendSecretsValidator>();
        services.AddSingleton<IValidateOptions<AppSecrets>, AppSecretsValidator>();

        ////services
        ////    .ConfigureAndValidateSingleton<AppSettings>(configuration, o => o.BindNonPublicProperties = false);

        services
            .Configure<DelobytesBackendSecrets>(configuration.GetSection($"{nameof(AppSecrets)}:{nameof(AppSecrets.DelobytesBackend)}"), o => o.BindNonPublicProperties = false)
            .Configure<AppSecrets>(configuration.GetSection(nameof(AppSecrets)), o => o.BindNonPublicProperties = false);

        return services;
    }

    /// <summary>
    /// Создаёт экземпляры всех настроек и проверяет значения до старта приложения.
    /// Используем вместо встроенного процесса валидации ValidateOnStart
    /// т.к. последний проверяет только в процессе запуска и обращения к объектам, что может привести к проблемам.
    /// </summary>
    public static IServiceCollection AddOptionsValidationOnStartup(this IServiceCollection services)
    {
        try
        {
            ServiceProvider provider = services.BuildServiceProvider();

            AppSettings? applicationOptions = provider.GetService<IOptions<AppSettings>>()?.Value;

            AppSecrets? appSecrets = provider.GetService<IOptions<AppSecrets>>()?.Value;
            DelobytesBackendSecrets? userWorkerSecrets = provider.GetService<IOptions<DelobytesBackendSecrets>>()?.Value;
        }
        catch (OptionsValidationException ex)
        {
            Console.WriteLine($"Error validating {ex.OptionsType.FullName}: {string.Join(", ", ex.Failures)}");
            throw;
        }

        return services;
    }
}
