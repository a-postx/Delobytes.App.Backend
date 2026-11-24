using System.Threading.RateLimiting;
using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Extensions;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;

namespace Delobytes.App.Backend.Composition;

internal static class ApiSurface
{
    public static WebApplicationBuilder AddApiSurface(this WebApplicationBuilder builder)
    {
        builder.Services.AddControllers()
            .ConfigureApiBehaviorOptions(options =>
            {
                // Model binding rejects a request before any handler runs; without this the
                // client receives ValidationProblemDetails (400) while FluentValidation
                // answers ErrorResponse (422) for the same class of mistake.
                options.InvalidModelStateResponseFactory = context =>
                {
                    Dictionary<string, string[]> errors = context.ModelState
                        .Where(entry => entry.Value is { Errors.Count: > 0 })
                        .GroupBy(entry => ErrorFieldNames.ToCamelCase(entry.Key))
                        .ToDictionary(
                            group => group.Key,
                            group => group
                                .SelectMany(entry => entry.Value!.Errors)
                                .Select(error => string.IsNullOrEmpty(error.ErrorMessage)
                                    ? ErrorCodes.Common.ValidationFailed.DefaultMessage
                                    : error.ErrorMessage)
                                .ToArray());

                    ErrorResponse body = ErrorResponse.FromCode(ErrorCodes.Common.ValidationFailed, errors: errors);

                    return new Microsoft.AspNetCore.Mvc.ObjectResult(body)
                    {
                        StatusCode = ErrorCodes.Common.ValidationFailed.Status,
                    };
                };
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            });

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.DescribeAllParametersInCamelCase();
            options.EnableAnnotations();

            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Delobytes App Backend API",
                Version = "v1",
                Description = "Delobytes e-commerce margin accounting system API",
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme.",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer"
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        // Limits password guessing per client address. Behind a reverse proxy this key is
        // only useful with ForwardedHeaders configured to trust that proxy.
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("login", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
        });

        builder.Services.AddCustomCors();

        return builder;
    }
}
