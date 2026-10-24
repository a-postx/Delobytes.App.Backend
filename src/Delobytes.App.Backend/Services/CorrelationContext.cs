using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Services;

/// <summary>
/// Scoped <see cref="ICorrelationContext"/> implementation.
/// Resolves the identifier from the current <see cref="HttpContext"/> first and falls back to
/// an explicitly set value, which is how MassTransit consume filters populate it for message
/// processing.
/// </summary>
public class CorrelationContext : ICorrelationContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private Guid? _messageCorrelationId;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationContext"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">HTTP context accessor.</param>
    public CorrelationContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
    public Guid? CorrelationId
    {
        get
        {
            object? fromHttpContext = _httpContextAccessor.HttpContext?.Items[CorrelationIdProvider.HttpContextItemKey];

            return fromHttpContext is Guid guid ? guid : _messageCorrelationId;
        }
    }

    /// <summary>
    /// Sets the correlation identifier for the current message-processing scope.
    /// </summary>
    /// <param name="correlationId">Correlation identifier.</param>
    public void SetCorrelationId(Guid? correlationId)
    {
        _messageCorrelationId = correlationId;
    }
}
