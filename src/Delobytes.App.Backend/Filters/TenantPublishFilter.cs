using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Contracts.Interfaces;
using MassTransit;

namespace Delobytes.App.Backend.Filters;

/// <summary>
/// MassTransit publish filter that adds tenant ID to message headers.
/// Automatically extracts TenantId from ITenantContext and adds it to every published message.
/// </summary>
/// <typeparam name="T">Message type.</typeparam>
public class TenantPublishFilter<T> : IFilter<PublishContext<T>> where T : class
{
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantPublishFilter{T}"/> class.
    /// </summary>
    /// <param name="tenantContext">Tenant context.</param>
    public TenantPublishFilter(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    /// <inheritdoc/>
    public async Task Send(PublishContext<T> context, IPipe<PublishContext<T>> next)
    {
        Guid? tenantId = _tenantContext.TenantId;

        if (tenantId.HasValue)
        {
            context.Headers.Set(TenantMessageHeaders.TenantId, tenantId.Value);
        }

        await next.Send(context);
    }

    /// <inheritdoc/>
    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("tenantPublish");
    }
}
