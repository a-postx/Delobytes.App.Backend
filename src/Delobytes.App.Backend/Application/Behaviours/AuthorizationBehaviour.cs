using System.Security.Claims;
using Delobytes.App.Backend.Identity.Domain.Enums;
using Delobytes.App.Backend.Identity.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Delobytes.App.Backend.Application.Behaviours;

/// <summary>
/// MediatR pipeline behavior that enforces role-based authorization for commands and queries
/// marked with IRequireRole interface.
/// </summary>
/// <typeparam name="TRequest">Request type.</typeparam>
/// <typeparam name="TResponse">Response type.</typeparam>
public class AuthorizationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuthorizationBehaviour<TRequest, TResponse>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationBehaviour{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">HTTP context accessor.</param>
    /// <param name="logger">Logger instance.</param>
    public AuthorizationBehaviour(
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuthorizationBehaviour<TRequest, TResponse>> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is IRequireRole requireRole)
        {
            HttpContext? httpContext = _httpContextAccessor.HttpContext;

            if (httpContext?.User?.Identity?.IsAuthenticated != true)
            {
                _logger.LogWarning(
                    "Authorization failed for {RequestType}: User is not authenticated",
                    typeof(TRequest).Name);
                throw new UnauthorizedAccessException("Пользователь не аутентифицирован.");
            }

            string? roleClaim = httpContext.User.FindFirstValue("role");

            if (string.IsNullOrEmpty(roleClaim))
            {
                _logger.LogWarning(
                    "Authorization failed for {RequestType}: Role claim not found",
                    typeof(TRequest).Name);
                throw new UnauthorizedAccessException("Роль пользователя не определена.");
            }

            if (!Enum.TryParse<Role>(roleClaim, out Role userRole))
            {
                _logger.LogWarning(
                    "Authorization failed for {RequestType}: Invalid role value {RoleClaim}",
                    typeof(TRequest).Name,
                    roleClaim);
                throw new UnauthorizedAccessException("Недопустимое значение роли.");
            }

            if (!requireRole.AllowedRoles.Contains(userRole))
            {
                string userId = httpContext.User.FindFirstValue("userId") ?? "unknown";
                string tenantId = httpContext.User.FindFirstValue("tenantId") ?? "unknown";

                _logger.LogWarning(
                    "Authorization failed for {RequestType}: User {UserId} in tenant {TenantId} with role {UserRole} is not authorized. Required roles: {RequiredRoles}",
                    typeof(TRequest).Name,
                    userId,
                    tenantId,
                    userRole,
                    string.Join(", ", requireRole.AllowedRoles));

                throw new UnauthorizedAccessException(
                    $"У вас недостаточно прав для выполнения этой операции. Требуется одна из ролей: {string.Join(", ", requireRole.AllowedRoles)}.");
            }

            _logger.LogDebug(
                "Authorization passed for {RequestType}: User role {UserRole}",
                typeof(TRequest).Name,
                userRole);
        }

        return await next();
    }
}
