namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Mutable accounting for one orders import run. The same instance is shared by every contributor,
/// so each sees the counters advanced by its peers. It reports what happened and never decides what
/// to do next.
/// </summary>
public sealed class RunCounters
{
    public int RequestsIssued { get; set; }

    public int InProcessWaits { get; set; }

    public TimeSpan TotalWaited { get; set; }

    public DateTimeOffset LastRequestStartedAt { get; set; }
}
