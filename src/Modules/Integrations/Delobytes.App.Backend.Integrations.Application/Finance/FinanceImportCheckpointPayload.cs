using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Application.Finance;

/// <summary>
/// Durable state of one finance import run: the window it covers, the page to continue from and
/// how many batches it has already published. Serialized into <see cref="JobCheckpoint.Payload"/>
/// by <see cref="FinanceImportCheckpointStore"/>, which owns the shape.
/// </summary>
public sealed class FinanceImportCheckpointPayload
{
    /// <summary>Gets or sets the first day of the window being read.</summary>
    public DateOnly? WindowFrom { get; set; }

    /// <summary>Gets or sets the last day of the window being read.</summary>
    public DateOnly? WindowEnd { get; set; }

    /// <summary>Gets or sets the cursor the next page must be requested with, if any.</summary>
    public string? Cursor { get; set; }

    /// <summary>Gets or sets how many batches the run has published so far.</summary>
    public int PublishedBatches { get; set; }

    /// <summary>Gets or sets whether the whole window has been read.</summary>
    public bool WindowCompleted { get; set; }

    /// <summary>Gets or sets the mapping diagnostics accumulated across the pages read.</summary>
    public FinanceMappingDiagnostics Diagnostics { get; set; } = new();

    /// <summary>Gets or sets the operator-supplied window start, when the run was started manually.</summary>
    public DateOnly? ExplicitFrom { get; set; }

    /// <summary>Gets or sets the operator-supplied window end, when the run was started manually.</summary>
    public DateOnly? ExplicitTo { get; set; }
}
