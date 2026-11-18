using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Integrations.Application.Models;

/// <summary>
/// One page of finance entries read from a channel.
/// </summary>
public sealed class FinanceEntriesPage
{
    /// <summary>Gets or sets the entries of the page.</summary>
    public IReadOnlyList<ChannelFinanceEntrySnapshot> Entries { get; set; } = Array.Empty<ChannelFinanceEntrySnapshot>();

    /// <summary>Gets or sets the cursor of the next page, or null when the document ended.</summary>
    public string? NextCursor { get; set; }

    /// <summary>Gets or sets the mapping diagnostics of the page.</summary>
    public FinanceMappingDiagnostics Diagnostics { get; set; } = new FinanceMappingDiagnostics();

    /// <summary>Gets or sets a value indicating whether the page is the last one.</summary>
    public bool IsComplete { get; set; }

    /// <summary>Gets or sets a value indicating whether the rate limiter deferred the request.</summary>
    public bool Throttled { get; set; }

    /// <summary>Gets or sets the instant the next attempt is allowed, when throttled.</summary>
    public DateTimeOffset? RetryAt { get; set; }
}
