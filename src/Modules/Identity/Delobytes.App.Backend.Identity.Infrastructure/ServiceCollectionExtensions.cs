using System.Text;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Infrastructure.Persistence;
using Delobytes.App.Backend.Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
namespace Delobytes.App.Backend.Identity.Infrastructure;

/// <summary>
/// Registers Identity module infrastructure services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Identity infrastructure services to the DI container.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration.</param>
    /// <param name="connectionString">Connection string.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string? connectionString,
        string? jwtSecretKey,
        string? yandexClientId,
        string? yandexClientSecret,
        string? googleClientId,
        string? googleClientSecret)
    {
        if (connectionString == null)
        {
            throw new InvalidOperationException("Connection string is not configured.");
        }

        if (jwtSecretKey == null)
        {
            throw new InvalidOperationException("Jwt secret key is not configured.");
        }

        // Register HttpContextAccessor for TenantContext
        services.AddHttpContextAccessor();

        // Register ITenantContext
        services.AddScoped<ITenantContext, TenantContext>();

        // Register JWT token service
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        // Register password hasher
        services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();

        // Register repositories
        services.AddScoped<IUserRepository, Persistence.Repositories.UserRepository>();
        services.AddScoped<ITenantRepository, Persistence.Repositories.TenantRepository>();
        services.AddScoped<ITenantMembershipRepository, Persistence.Repositories.TenantMembershipRepository>();

        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.MigrationsHistoryTable("__IdentityMigrationsHistory", "identity")));

        // Register Yandex OAuth service
        services.AddHttpClient("YandexOAuth");
        services.AddScoped<IYandexOAuthService>(sp =>
        {
            IHttpClientFactory factory = sp.GetRequiredService<IHttpClientFactory>();
            HttpClient httpClient = factory.CreateClient("YandexOAuth");
            return new YandexOAuthService(
                httpClient,
                yandexClientId ?? string.Empty,
                yandexClientSecret ?? string.Empty);
        });

        // Register Google OAuth service
        services.AddHttpClient("GoogleOAuth");
        services.AddScoped<IGoogleOAuthService>(sp =>
        {
            IHttpClientFactory factory = sp.GetRequiredService<IHttpClientFactory>();
            HttpClient httpClient = factory.CreateClient("GoogleOAuth");
            return new GoogleOAuthService(
                httpClient,
                googleClientId ?? string.Empty,
                googleClientSecret ?? string.Empty);
        });

        // Configure JWT Authentication
        IConfigurationSection jwtSettings = configuration.GetSection("JwtSettings");
        string issuer = jwtSettings["Issuer"] ?? "Delobytes.App.Backend";
        string audience = jwtSettings["Audience"] ?? "Delobytes.App.Frontend";

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
                ClockSkew = TimeSpan.FromSeconds(1),
            };
        });

        services.AddAuthorization(options =>
        {
            // Register policies for each role
            options.AddPolicy("RequireRole_Administrator", policy =>
                policy.Requirements.Add(new Authorization.RoleRequirement(Domain.Enums.Role.Administrator)));

            options.AddPolicy("RequireRole_Manager", policy =>
                policy.Requirements.Add(new Authorization.RoleRequirement(Domain.Enums.Role.Administrator, Domain.Enums.Role.Manager)));

            options.AddPolicy("RequireRole_ReadOnly", policy =>
                policy.Requirements.Add(new Authorization.RoleRequirement(Domain.Enums.Role.Administrator, Domain.Enums.Role.Manager, Domain.Enums.Role.ReadOnly)));
        });

        // Register authorization handler
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, Authorization.RoleAuthorizationHandler>();

        return services;
    }
}
