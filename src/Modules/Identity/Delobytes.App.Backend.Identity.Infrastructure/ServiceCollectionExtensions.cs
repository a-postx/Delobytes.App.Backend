using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Infrastructure.Persistence;
using Delobytes.App.Backend.Identity.Infrastructure.Services;
using Delobytes.AspNetCore.Logging;
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
    /// <param name="jwtSecretKey">Секретный ключ для шифрования токенов.</param>
    /// <param name="yandexClientId">Идентификатор клиента Яндекс ID.</param>
    /// <param name="yandexClientSecret">Секрет клиента Яндекс ID.</param>
    /// <param name="googleClientId">Идентификатор клиента Google ID.</param>
    /// <param name="googleClientSecret">Секрет клиента Google ID.</param>
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

        // Register JWT token service
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        // Register password hasher
        services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();

        // Register repositories
        services.AddScoped<IUserRepository, Persistence.Repositories.UserRepository>();
        services.AddScoped<ITenantRepository, Persistence.Repositories.TenantRepository>();
        services.AddScoped<ITenantMembershipRepository, Persistence.Repositories.TenantMembershipRepository>();
        services.AddScoped<IInvitationRepository, Persistence.Repositories.InvitationRepository>();

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

        // Avoid claim mapping to old ms soap namespaces.
        // Avoid replace "role" by "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
        // This is required to be instantiated before the OpenIdConnectOptions starts getting configured.
        JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
        JwtSecurityTokenHandler.DefaultMapInboundClaims = false;

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
                ClockSkew = TimeSpan.FromSeconds(1)
            };
        });

        services.AddClaimsLogging(options =>
        {
            options.UserIdClaimName = "sub";
            options.TenantIdClaimName = "tenantId";
        });

        // Authorization for module commands and queries is enforced by the MediatR
        // AuthorizationBehaviour pipeline in the host (Contracts.Authorization.IRequireRole).
        // The plain [Authorize] attribute on controllers relies on the default policy
        // registered by the framework, so no named policies are defined here.
        services.AddAuthorization();

        return services;
    }
}
