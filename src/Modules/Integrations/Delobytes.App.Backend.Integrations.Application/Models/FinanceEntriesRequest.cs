namespace Delobytes.App.Backend.Integrations.Application.Models;

/// <summary>
/// A request for one page of a finance document.
/// </summary>
public sealed class FinanceEntriesRequest
{
    /// <summary>Gets or sets the first document date, channel-local.</summary>
    public DateOnly From { get; set; }

    /// <summary>Gets or sets the last document date, channel-local.</summary>
    public DateOnly To { get; set; }

    /// <summary>Gets or sets the opaque page cursor returned by the previous page.</summary>
    public string? Cursor { get; set; }
}
