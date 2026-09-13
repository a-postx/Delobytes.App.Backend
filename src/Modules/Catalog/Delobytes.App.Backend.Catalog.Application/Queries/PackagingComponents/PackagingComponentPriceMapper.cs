using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Queries.PackagingComponents;

/// <summary>
/// Shared mapping of price versions to the client-facing DTO.
/// </summary>
internal static class PackagingComponentPriceMapper
{
    public static PackagingComponentPriceDto? Map(PackagingComponentPrice? price)
    {
        if (price == null)
        {
            return null;
        }

        return new PackagingComponentPriceDto
        {
            Id = price.Id,
            PricePerUnit = price.PricePerUnit,
            SupplierId = price.SupplierId,
            SupplierName = price.Supplier?.Name,
            ValidFrom = price.ValidFrom.ToString("yyyy-MM-dd"),
        };
    }
}
