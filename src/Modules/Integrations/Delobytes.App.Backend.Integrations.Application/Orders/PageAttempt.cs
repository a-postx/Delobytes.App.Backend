using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// One paced request attempt: either the page that came back, or the reason the attempt stopped
/// before any page was read.
/// </summary>
internal abstract record PageAttempt
{
    public sealed record Page : PageAttempt
    {
        public required OrdersPage Value { get; init; }
    }

    public sealed record Stop : PageAttempt
    {
        public required StopCause Cause { get; init; }

        public required DateTimeOffset ResumeAt { get; init; }
    }
}
