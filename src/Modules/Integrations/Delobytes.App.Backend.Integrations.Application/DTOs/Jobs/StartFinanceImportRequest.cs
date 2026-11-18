namespace Delobytes.App.Backend.Integrations.Application.DTOs.Jobs;

/// <summary>
/// Request body of the operator-triggered finance re-import.
/// </summary>
public sealed class StartFinanceImportRequest
{
    /// <summary>Gets or sets the connection to import from.</summary>
    public Guid ConnectionId { get; set; }

    /// <summary>Gets or sets the job type token: <c>wb-finance-report-import</c> or <c>ozon-finance-accruals-import</c>.</summary>
    public string Type { get; set; } = default!;

    /// <summary>Gets or sets the first date to fetch, channel-local.</summary>
    public DateOnly FromDate { get; set; }

    /// <summary>Gets or sets the last date to fetch, channel-local.</summary>
    public DateOnly ToDate { get; set; }
}
