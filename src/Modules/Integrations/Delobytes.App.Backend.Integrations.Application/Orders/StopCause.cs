namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Why a fetch stopped before its window was read whole. The absence of a stop is expressed by
/// returning a read case instead, so there is no "completed" member to misread as a cause.
/// </summary>
public enum StopCause
{
    Throttle = 0,
    TimeBudget = 1,
    RequestBudget = 2,
}
