namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>Filters shared by the read queries.</summary>
public sealed class PnlQueryFilter
{
    public Guid? ChannelId { get; set; }

    public Guid? ProductId { get; set; }

    /// <summary>Gets or sets the channel product filter, independent of the catalogue product.</summary>
    public Guid? ChannelProductId { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    public string? State { get; set; }

    /// <summary>
    /// Gets or sets the completeness filter: <c>complete</c>, <c>provisional</c>, <c>overdue</c> or
    /// <c>closedWithException</c>. Omitted means <c>complete</c>, so a report never mixes provisional
    /// figures into its totals.
    /// </summary>
    public string? Completeness { get; set; }
}
