namespace Delobytes.App.Backend.Integrations.Contracts.Models;

/// <summary>
/// Normalized financial fact reported by a marketplace, ready to be appended to the Finance
/// journal. Contract model without dependencies on EF Core or HTTP client types.
/// </summary>
public record ChannelFinanceEntrySnapshot
{
    /// <summary>
    /// Gets the external document the fact was read from.
    /// </summary>
    public FinanceEntrySource Source { get; init; }

    /// <summary>
    /// Gets the identity of the report instance the fact belongs to. Wildberries:
    /// <c>reportId:createDate</c>. Ozon: an empty string, because an accrual is never re-issued.
    /// </summary>
    public string ReportInstanceKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets the channel-side identity of the row: the Wildberries <c>rrdId</c> or the Ozon
    /// <c>accrual_id</c>.
    /// </summary>
    public string ExternalEntryId { get; init; } = default!;

    /// <summary>
    /// Gets the channel-side order identity, where the document carries one.
    /// </summary>
    public string? ExternalOrderId { get; init; }

    /// <summary>
    /// Gets the channel-side unit identity: the Wildberries <c>srid</c> or the Ozon posting number.
    /// </summary>
    public string? ExternalLineId { get; init; }

    /// <summary>
    /// Gets the channel-side product identity: the Wildberries <c>nmId</c> or the Ozon <c>sku</c>.
    /// </summary>
    public string ExternalProductId { get; init; } = default!;

    /// <summary>
    /// Gets whether the entry is a component or a control total.
    /// </summary>
    public FinanceEntryKind Kind { get; init; }

    /// <summary>
    /// Gets the business meaning of the amount.
    /// </summary>
    public FinanceEntryType Type { get; init; }

    /// <summary>
    /// Gets the absolute amount. The sign is expressed by <see cref="Direction"/>.
    /// </summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// Gets which way <see cref="Amount"/> moves the result.
    /// </summary>
    public FinanceEntryDirection Direction { get; init; }

    /// <summary>
    /// Gets the entry currency.
    /// </summary>
    public string Currency { get; init; } = default!;

    /// <summary>
    /// Gets the date the operation happened per the channel.
    /// </summary>
    public DateOnly DocumentDate { get; init; }

    /// <summary>
    /// Gets the first day of the reporting period the document covers.
    /// </summary>
    public DateOnly PeriodFrom { get; init; }

    /// <summary>
    /// Gets the last day of the reporting period the document covers.
    /// </summary>
    public DateOnly PeriodTo { get; init; }
}
