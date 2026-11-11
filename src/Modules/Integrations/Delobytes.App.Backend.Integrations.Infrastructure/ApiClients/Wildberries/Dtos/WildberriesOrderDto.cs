using System.Text.Json.Serialization;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Wildberries.Dtos;

/// <summary>
/// One row of the <c>/api/v1/supplier/orders</c> response: one ordered unit.
/// </summary>
internal sealed class WildberriesOrderDto
{
    [JsonPropertyName("date")]
    public string? Date { get; init; }

    [JsonPropertyName("lastChangeDate")]
    public string? LastChangeDate { get; init; }

    [JsonPropertyName("warehouseName")]
    public string? WarehouseName { get; init; }

    [JsonPropertyName("warehouseType")]
    public string? WarehouseType { get; init; }

    [JsonPropertyName("regionName")]
    public string? RegionName { get; init; }

    [JsonPropertyName("supplierArticle")]
    public string? SupplierArticle { get; init; }

    [JsonPropertyName("nmId")]
    public long NmId { get; init; }

    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    [JsonPropertyName("techSize")]
    public string? TechSize { get; init; }

    [JsonPropertyName("totalPrice")]
    public decimal? TotalPrice { get; init; }

    [JsonPropertyName("priceWithDisc")]
    public decimal? PriceWithDisc { get; init; }

    [JsonPropertyName("finishedPrice")]
    public decimal? FinishedPrice { get; init; }

    [JsonPropertyName("isCancel")]
    public bool IsCancel { get; init; }

    [JsonPropertyName("orderType")]
    public string? OrderType { get; init; }

    [JsonPropertyName("gNumber")]
    public string? GNumber { get; init; }

    [JsonPropertyName("srid")]
    public string? Srid { get; init; }

    [JsonPropertyName("sticker")]
    public string? Sticker { get; init; }
}
