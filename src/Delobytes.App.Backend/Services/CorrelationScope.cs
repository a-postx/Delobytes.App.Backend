using Delobytes.App.Backend.Contracts.Interfaces;
using Serilog.Context;

namespace Delobytes.App.Backend.Services;

/// <summary>
/// Scoped <see cref="ICorrelationScope"/> implementation.
/// Depends on the concrete <see cref="CorrelationContext"/> because it needs the setter.
/// </summary>
public class CorrelationScope : ICorrelationScope
{
    private readonly CorrelationContext _correlationContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationScope"/> class.
    /// </summary>
    /// <param name="correlationContext">Correlation context.</param>
    public CorrelationScope(CorrelationContext correlationContext)
    {
        _correlationContext = correlationContext;
    }

    /// <inheritdoc/>
    public IDisposable BeginNew()
    {
        // Restoring the previous value (rather than hard-resetting to null) keeps this safe to
        // nest, e.g. a future caller wrapping several BeginNew() calls inside an outer one.
        Guid? previousCorrelationId = _correlationContext.CorrelationId;
        Guid correlationId = CorrelationIdProvider.Create();

        _correlationContext.SetCorrelationId(correlationId);
        IDisposable logScope = LogContext.PushProperty("CorrelationId", correlationId);

        return new Handle(_correlationContext, logScope, previousCorrelationId);
    }

    private sealed class Handle : IDisposable
    {
        private readonly CorrelationContext _correlationContext;
        private readonly IDisposable _logScope;
        private readonly Guid? _previousCorrelationId;
        private bool _disposed;

        public Handle(CorrelationContext correlationContext, IDisposable logScope, Guid? previousCorrelationId)
        {
            _correlationContext = correlationContext;
            _logScope = logScope;
            _previousCorrelationId = previousCorrelationId;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _correlationContext.SetCorrelationId(_previousCorrelationId);
            _logScope.Dispose();
        }
    }
}
