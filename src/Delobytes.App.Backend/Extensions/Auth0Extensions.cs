using Delobytes.App.Backend.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Delobytes.App.Backend.Extensions;

/// <summary>
/// Extension methods for configuring Auth0 JWT Bearer authentication.
/// </summary>
internal static class Auth0Extensions
{
    /// <summary>
    /// Adds Auth0 JWT Bearer authentication to the service collection.
    /// Auth0 handles EXCLUSIVELY user identity verification (authentication).
    /// Authorization (roles, tenants) is implemented in the application layer.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="auth0Options">Auth0 configuration options.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddAuth0Authentication(this IServiceCollection services, Auth0Options auth0Options)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Auth0 OIDC discovery endpoint: https://{domain}/.well-known/openid-configuration
                options.Authority = auth0Options.Authority;
                options.Audience = auth0Options.Audience;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // Validates that the token was issued by Auth0 (authority)
                    ValidateIssuer = true,
                    ValidIssuer = auth0Options.Authority,

                    // Validates that the token is intended for our API
                    ValidateAudience = true,
                    ValidAudience = auth0Options.Audience,

                    // Validates token lifetime (exp / nbf claims)
                    ValidateLifetime = true,

                    // Validates the signing key via OIDC JWKS endpoint
                    ValidateIssuerSigningKey = true,

                    // Clock skew tolerance (default 5 min, keep it small for security)
                    ClockSkew = TimeSpan.FromMinutes(1),
                };

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        ILogger logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<JwtBearerEvents>>();
                        logger.LogWarning(
                            context.Exception,
                            "JWT authentication failed for request {Method} {Path}",
                            context.HttpContext.Request.Method,
                            context.HttpContext.Request.Path);
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        ILogger logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<JwtBearerEvents>>();
                        logger.LogDebug(
                            "JWT token validated for subject={Subject}",
                            context.Principal?.FindFirst("sub")?.Value ?? "unknown");
                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization();

        return services;
    }
}
