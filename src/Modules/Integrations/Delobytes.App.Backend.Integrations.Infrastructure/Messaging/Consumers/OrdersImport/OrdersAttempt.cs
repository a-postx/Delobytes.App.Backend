using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// One paced request attempt: either the page that came back, or the reason the attempt stopped
/// before any page was read.
/// </summary>
internal sealed class OrdersAttempt
{
    private OrdersAttempt(OrdersPage? page, StopReason stopReason, DateTimeOffset? resumeAt)
    {
        Page = page;
        StopReason = stopReason;
        ResumeAt = resumeAt;
    }

    public OrdersPage? Page { get; }

    public StopReason StopReason { get; }

    public DateTimeOffset? ResumeAt { get; }

    public bool IsStopped => Page == null;

    public static OrdersAttempt Received(OrdersPage page)
    {
        return new OrdersAttempt(page, StopReason.Completed, null);
    }

    public static OrdersAttempt Stopped(StopReason stopReason, DateTimeOffset resumeAt)
    {
        return new OrdersAttempt(null, stopReason, resumeAt);
    }
}
