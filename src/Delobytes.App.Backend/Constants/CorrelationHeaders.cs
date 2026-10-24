namespace Delobytes.App.Backend.Constants;

/// <summary>
/// Header names used for request correlation across HTTP and the message bus.
/// </summary>
public static class CorrelationHeaders
{
    /// <summary>
    /// Correlation identifier header. Always server-generated (an incoming client-supplied value
    /// is not trusted) and echoed on every response, including error responses. Reused verbatim as
    /// the MassTransit message header so the same chain is greppable in both transports. Carries a
    /// GUID in its canonical "D" string form.
    /// </summary>
    public const string CorrelationId = "X-Correlation-Id";
}
