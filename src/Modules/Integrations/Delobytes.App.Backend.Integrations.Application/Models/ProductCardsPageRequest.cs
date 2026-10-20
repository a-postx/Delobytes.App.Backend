using Delobytes.App.Backend.Integrations.Application.DTOs;

namespace Delobytes.App.Backend.Integrations.Application.Models;

/// <summary>
/// Channel-neutral request for one page of product cards, used by
/// <see cref="Interfaces.IProductsSource"/>.
/// </summary>
public class ProductCardsPageRequest
{
    /// <summary>
    /// Gets or sets the pagination cursor. Null requests the first page.
    /// </summary>
    public ProductCardsCursor? Cursor { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of cards to retrieve.
    /// </summary>
    public int Limit { get; set; }
}
