using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Integrations.Application.DTOs;

/// <summary>
/// Represents product cards data retrieved from a marketplace channel.
/// </summary>
public class ProductCardsData
{
    /// <summary>
    /// Gets or sets the collection of product card snapshots.
    /// </summary>
    public ICollection<WildberriesCardSnapshot> Cards { get; set; } = new List<WildberriesCardSnapshot>();

    /// <summary>
    /// Gets or sets the pagination cursor for retrieving the next page.
    /// Null if this is the last page.
    /// </summary>
    public ProductCardsCursor? NextCursor { get; set; }

    /// <summary>
    /// Gets or sets the total count of cards available.
    /// </summary>
    public int TotalCount { get; set; }
}
