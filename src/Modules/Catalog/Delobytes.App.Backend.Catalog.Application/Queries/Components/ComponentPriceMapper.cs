using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Components;

/// <summary>
/// Shared mapping of price versions to the client-facing DTO.
/// </summary>
internal static class ComponentPriceMapper
{
    public static ComponentPriceDto? Map(ComponentPrice? price)
    {
        if (price == null)
        {
            return null;
        }

        return new ComponentPriceDto
        {
            Id = price.Id,
            PricePerUnit = price.PricePerUnit,
            SupplierId = price.SupplierId,
            SupplierName = price.Supplier?.Name,
            ValidFrom = price.ValidFrom.ToString("yyyy-MM-dd"),
        };
    }
}
