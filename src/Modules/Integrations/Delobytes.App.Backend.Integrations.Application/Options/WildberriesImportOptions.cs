namespace Delobytes.App.Backend.Integrations.Application.Options;

/// <summary>
/// Configuration options for Wildberries product import.
/// </summary>
public class WildberriesImportOptions
{
    /// <summary>
    /// Number of product cards to retrieve per API call. Must be between 1 and 100.
    /// </summary>
    public int BatchSize { get; set; } = 50;
}
