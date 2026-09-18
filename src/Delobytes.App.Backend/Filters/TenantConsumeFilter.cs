using Delobytes.App.Backend.Constants;
using Delobytes.App.Backend.Services;
using MassTransit;

namespace Delobytes.App.Backend.Filters;

/// <summary>
/// MassTransit consume filter that extracts tenant ID from message headers
/// and sets it in MessageTenantContext for the duration of message processing.
/// Ensures tenant isolation in message consumers.
/// </summary>
/// <typeparam name="T">Message type.</typeparam>
public class TenantConsumeFilter<T> : IFilter<ConsumeContext<T>> where T : class
{
    private readonly MessageTenantContext _messageTenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantConsumeFilter{T}"/> class.
    /// </summary>
    /// <param name="messageTenantContext">Message-based tenant context.</param>
    public TenantConsumeFilter(MessageTenantContext messageTenantContext)
    {
        _messageTenantContext = messageTenantContext;
    }

    /// <inheritdoc/>
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        Guid? tenantId = context.Headers.Get<Guid>(TenantMessageHeaders.TenantId);

        if (tenantId.HasValue)
        {
            _messageTenantContext.SetTenantId(tenantId.Value);
        }

        try
        {
            await next.Send(context);
        }
        finally
        {
            // Always clear tenant context after message processing to prevent leakage
            _messageTenantContext.Clear();
        }
    }

    /// <inheritdoc/>
    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("tenantConsume");
    }
}
