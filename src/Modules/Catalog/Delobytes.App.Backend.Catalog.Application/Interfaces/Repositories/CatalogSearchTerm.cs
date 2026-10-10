namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

/// <summary>
/// Normalisation shared by the catalog repositories that accept free-text search, so every list
/// endpoint interprets the same input identically.
/// </summary>
public static class CatalogSearchTerm
{
    /// <summary>
    /// Search terms longer than the longest searchable column are pointless: Product.Name caps at
    /// 200 characters, so a longer term cannot match anything anyway.
    /// </summary>
    public const int MaxLength = 200;

    /// <summary>
    /// Reduces a raw query-string value to the term used for matching: trims surrounding
    /// whitespace and caps the length.
    /// </summary>
    /// <param name="search">Raw value as received from the client.</param>
    /// <returns>The normalised term, or null when the input carries no filter at all.</returns>
    public static string? Normalize(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        string trimmed = search.Trim();

        return trimmed.Length > MaxLength
            ? trimmed.Substring(0, MaxLength)
            : trimmed;
    }
}
