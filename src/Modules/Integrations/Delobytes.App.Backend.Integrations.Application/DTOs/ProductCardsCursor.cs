namespace Delobytes.App.Backend.Integrations.Application.DTOs;

/// <summary>
/// Represents a cursor for pagination of product cards.
/// </summary>
public class ProductCardsCursor
{
    /// <summary>
    /// Gets or sets the UTC timestamp for cursor-based pagination.
    /// </summary>
    public string UpdatedAt { get; set; } = default!;

    /// <summary>
    /// Gets or sets the product identifier for cursor-based pagination.
    /// For Wildberries, this is nmID.
    /// </summary>
    public long ProductId { get; set; }

    /// <summary>
    /// Gets or sets an opaque string cursor. Ozon pages by <c>last_id</c>, which is a string, and
    /// does not use the timestamp/number pair Wildberries does.
    /// </summary>
    public string? Token { get; set; }
}
