namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>Filters shared by the read queries.</summary>
public sealed class PnlQueryFilter
{
    /// <summary>Gets the maximum number of identifiers a multi-product filter accepts.</summary>
    public const int MaxProductIds = 10;

    public Guid? ChannelId { get; set; }

    public Guid? ProductId { get; set; }

    /// <summary>
    /// Gets or sets the optional multi-product filter, the trend and drill-down case: one request
    /// reads several selected products. Mutually exclusive with <see cref="ProductId"/>; an empty
    /// array means "no filter" and must not be read as "no products".
    /// </summary>
    public Guid[]? ProductIds { get; set; }

    /// <summary>Gets or sets the channel product filter, independent of the catalogue product.</summary>
    public Guid? ChannelProductId { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    public string? State { get; set; }

    /// <summary>
    /// Gets or sets the completeness selection: <c>all</c>, <c>final</c>, <c>provisional</c>, or one
    /// exact state — <c>complete</c>, <c>overdue</c>, <c>closedWithException</c>. Omitted means
    /// <c>final</c>: provisional figures never blend into a report, while a recorded exception stays
    /// visible.
    /// </summary>
    public string? Completeness { get; set; }
}
