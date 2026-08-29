using System.Security.Claims;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Delobytes.App.Backend.Identity.Infrastructure.Services;

/// <summary>
/// Implementation of ITenantContext that extracts TenantId from JWT claims.
/// </summary>
public class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantContext"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">HTTP context accessor.</param>
    public TenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
    public Guid? TenantId
    {
        get
        {
            string? tenantIdClaim = _httpContextAccessor.HttpContext?.User
                .FindFirst("tenantId")?.Value;

            if (string.IsNullOrEmpty(tenantIdClaim))
            {
                return null;
            }

            return Guid.TryParse(tenantIdClaim, out Guid tenantId) ? tenantId : null;
        }
    }
}
