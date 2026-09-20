using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Services;

/// <summary>
/// Scoped <see cref="ICorrelationContext"/> implementation.
/// Resolves the identifier from the current <see cref="HttpContext"/> first and falls back to
/// an explicitly set value, which is how MassTransit consume filters populate it for message
/// processing. Clearing in the consume filter is what prevents the value from leaking into the
/// next message handled by the same consumer instance.
/// </summary>
public class CorrelationContext : ICorrelationContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private string? _messageCorrelationId;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationContext"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">HTTP context accessor.</param>
    public CorrelationContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
    public string? CorrelationId
    {
        get
        {
            string? fromHttpContext = _httpContextAccessor.HttpContext?.Items[CorrelationIdProvider.HttpContextItemKey] as string;

            return string.IsNullOrEmpty(fromHttpContext) ? _messageCorrelationId : fromHttpContext;
        }
    }

    /// <summary>
    /// Sets the correlation identifier for the current message-processing scope.
    /// </summary>
    /// <param name="correlationId">Correlation identifier.</param>
    public void SetCorrelationId(string? correlationId)
    {
        _messageCorrelationId = correlationId;
    }
}
