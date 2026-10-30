namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// How an orders fetch ended. <see cref="Completed"/> is the only value that means the requested
/// window was fully read.
/// </summary>
public enum StopReason
{
    Completed,
    Throttled,
    Budget,
}
