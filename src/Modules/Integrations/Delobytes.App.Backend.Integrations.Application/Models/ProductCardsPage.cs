using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Integrations.Application.Models;

/// <summary>
/// Channel-neutral page of product cards returned by <see cref="Interfaces.IProductsSource"/>.
/// </summary>
public class ProductCardsPage
{
    /// <summary>
    /// Gets or sets the product cards retrieved for this page.
    /// </summary>
    public List<WildberriesCardSnapshot> Cards { get; set; } = new();

    /// <summary>
    /// Gets or sets the cursor for the next page. Null if this is the last page.
    /// </summary>
    public ProductCardsCursor? NextCursor { get; set; }

    /// <summary>
    /// Gets or sets the total count of cards available, where the source reports one.
    /// </summary>
    public int TotalCount { get; set; }
}
