namespace Delobytes.App.Backend.Sales.Application.Models;

/// <summary>
/// Request body of the bounded order line re-publish.
/// </summary>
public sealed class RepublishOrderLinesRequest
{
    /// <summary>Gets or sets the optional channel filter.</summary>
    public Guid? ChannelId { get; set; }

    /// <summary>Gets or sets the optional first order date, tenant-local (inclusive).</summary>
    public DateOnly? FromDate { get; set; }

    /// <summary>Gets or sets the optional last order date, tenant-local (inclusive).</summary>
    public DateOnly? ToDate { get; set; }

    /// <summary>Gets or sets the continuation token returned by the previous page.</summary>
    public string? ContinuationToken { get; set; }

    /// <summary>Gets or sets the requested page size, capped by the handler.</summary>
    public int? MaxLines { get; set; }
}
