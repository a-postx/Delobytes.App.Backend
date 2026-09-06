namespace Delobytes.App.Backend.Sales.Domain.Enums;

/// <summary>
/// Represents the status of an order.
/// </summary>
public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Delivered = 2,
    Returned = 3,
    Cancelled = 4
}
