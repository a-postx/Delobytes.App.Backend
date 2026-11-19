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
}
