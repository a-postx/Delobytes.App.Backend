using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Identity.Infrastructure.Services;

namespace Delobytes.App.Backend.Services;

/// <summary>
/// Composite tenant context that tries HTTP context first, then message context.
/// Allows tenant resolution in both HTTP requests and MassTransit message consumers.
/// </summary>
public class CompositeTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly MessageTenantContext _messageTenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeTenantContext"/> class.
    /// </summary>
    /// <param name="httpTenantContext">HTTP-based tenant context.</param>
    /// <param name="messageTenantContext">Message-based tenant context.</param>
    public CompositeTenantContext(IHttpContextAccessor httpTenantContext, MessageTenantContext messageTenantContext)
    {
        _httpContextAccessor = httpTenantContext;
        _messageTenantContext = messageTenantContext;
    }

    /// <inheritdoc/>
    /// <inheritdoc/>
    public Guid? TenantId
    {
        get
        {
            string? tenantIdClaim = _httpContextAccessor.HttpContext?.User
                .FindFirst("tenantId")?.Value;

            if (!string.IsNullOrEmpty(tenantIdClaim) && Guid.TryParse(tenantIdClaim, out Guid tenantId))
            {
                return tenantId;
            }
            else
            {
                return _messageTenantContext.TenantId;
            }
        }
    }
}
