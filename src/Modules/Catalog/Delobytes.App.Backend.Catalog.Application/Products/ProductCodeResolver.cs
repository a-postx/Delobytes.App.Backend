using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Numbering;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Catalog.Application.Products;

/// <summary>
/// Issues the immutable product code and picks the internal SKU for a card that becomes a new
/// product: the trimmed vendor code when it is free, otherwise the code itself.
/// </summary>
/// <remarks>
/// A code used as a SKU can still collide, because Product.Sku is user-editable and a number
/// typed in by hand may equal a code the allocator has not issued yet. Such a collision burns the
/// number and moves to the next one; five of them mean the catalogue cannot be told apart from the
/// numbering scheme, and the card fails instead of guessing.
/// </remarks>
public class ProductCodeResolver
{
    /// <summary>
    /// How many codes a card may burn while looking for one that is also free as a SKU. Each
    /// discarded number is a gap in the product numbering, which is permitted.
    /// </summary>
    private const int MaxCodeAttempts = 5;

    private readonly IProductImportStore _store;
    private readonly INumberingSchemeProvider _numberingSchemes;
    private readonly INumberAllocator _numberAllocator;
    private readonly ILogger<ProductCodeResolver> _logger;

    // The instance lives for one batch, and the default scheme does not change within it, so it is
    // resolved once instead of on every allocation.
    private NumberingScheme? _scheme;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductCodeResolver"/> class.
    /// </summary>
    /// <param name="store">Persistence port of the import.</param>
    /// <param name="numberingSchemes">Resolves the scheme new product codes are issued from.</param>
    /// <param name="numberAllocator">
    /// Issues new product codes. It runs inside the batch transaction the store owns, so the
    /// numbers stay durable together with the products they belong to.
    /// </param>
    /// <param name="logger">Logger instance.</param>
    public ProductCodeResolver(
        IProductImportStore store,
        INumberingSchemeProvider numberingSchemes,
        INumberAllocator numberAllocator,
        ILogger<ProductCodeResolver> logger)
    {
        _store = store;
        _numberingSchemes = numberingSchemes;
        _numberAllocator = numberAllocator;
        _logger = logger;
    }

    /// <summary>Issues the code and picks the SKU of a new product.</summary>
    /// <param name="offerId">Seller article the card carries, when it carries one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The product code and the internal SKU to store with it.</returns>
    public async Task<(string Code, string Sku)> ResolveAsync(string? offerId, CancellationToken cancellationToken)
    {
        string candidate = ProductCardNormalizer.TrimOfferId(offerId);
        string code = await AllocateAsync(cancellationToken);

        if (candidate.Length > 0
            && await _store.FindProductBySkuAsync(candidate, cancellationToken) == null)
        {
            return (code, candidate);
        }

        for (int attempt = 1; attempt <= MaxCodeAttempts; attempt++)
        {
            if (await _store.FindProductBySkuAsync(code, cancellationToken) == null)
            {
                return (code, code);
            }

            if (attempt == MaxCodeAttempts)
            {
                break;
            }

            _logger.LogWarning(
                "Code {Code} is already used as a SKU by another product in this tenant. Allocating the next code.",
                code);

            code = await AllocateAsync(cancellationToken);
        }

        throw new AppException(CatalogErrorCodes.ProductCodeAllocationFailed);
    }

    private async Task<string> AllocateAsync(CancellationToken cancellationToken)
    {
        _scheme ??= await _numberingSchemes.GetDefaultProductSchemeAsync(cancellationToken);

        return await _numberAllocator.AllocateAsync(_scheme.Id, cancellationToken);
    }
}
