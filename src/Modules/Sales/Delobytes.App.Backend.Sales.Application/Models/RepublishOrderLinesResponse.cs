namespace Delobytes.App.Backend.Sales.Application.Models;

/// <summary>
/// The outcome of one bounded re-publish request.
/// </summary>
public sealed class RepublishOrderLinesResponse
{
    /// <summary>Gets or sets the number of lines whose change event was published.</summary>
    public int PublishedLines { get; set; }

    /// <summary>Gets or sets the number of published event chunks.</summary>
    public int ChunksPublished { get; set; }

    /// <summary>
    /// Gets or sets the continuation token to pass to the next request, or null when the page was
    /// the last one.
    /// </summary>
    public string? NextContinuationToken { get; set; }
}
