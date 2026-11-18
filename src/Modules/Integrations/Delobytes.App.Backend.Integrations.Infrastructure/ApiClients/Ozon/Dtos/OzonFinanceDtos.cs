using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;

/// <summary>
/// One page of the Ozon daily accruals response. The method is a beta, so unknown members are
/// ignored by the serializer.
/// </summary>
internal sealed class OzonAccrualsResponse
{
    [JsonPropertyName("accruals")]
    public List<OzonAccrual>? Accruals { get; set; }

    [JsonPropertyName("last_id")]
    public string? LastId { get; set; }
}

internal sealed class OzonAccrual
{
    [JsonPropertyName("accrual_id")]
    public long AccrualId { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("total_amount")]
    public OzonMoney? TotalAmount { get; set; }

    [JsonPropertyName("unit_number")]
    public string? UnitNumber { get; set; }

    [JsonPropertyName("accrued_category")]
    public string? AccruedCategory { get; set; }

    [JsonPropertyName("posting")]
    public OzonAccrualPosting? Posting { get; set; }

    [JsonPropertyName("item_fees")]
    public OzonAccrualItemFees? ItemFees { get; set; }
}

internal sealed class OzonAccrualPosting
{
    [JsonPropertyName("products")]
    public List<OzonAccrualProduct>? Products { get; set; }
}

internal sealed class OzonAccrualProduct
{
    [JsonPropertyName("sku")]
    public long Sku { get; set; }

    [JsonPropertyName("delivery")]
    public OzonAccrualDelivery? Delivery { get; set; }

    [JsonPropertyName("commission")]
    public OzonAccrualCommission? Commission { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}

internal sealed class OzonAccrualDelivery
{
    [JsonPropertyName("total_accrued")]
    public OzonMoney? TotalAccrued { get; set; }

    [JsonPropertyName("services")]
    public List<OzonAccrualService>? Services { get; set; }
}

internal sealed class OzonAccrualService
{
    [JsonPropertyName("type_id")]
    public int TypeId { get; set; }

    [JsonPropertyName("accrued")]
    public OzonMoney? Accrued { get; set; }
}

internal sealed class OzonAccrualCommission
{
    [JsonPropertyName("sale_price")]
    public OzonMoney? SalePrice { get; set; }

    [JsonPropertyName("sale_commission")]
    public OzonMoney? SaleCommission { get; set; }

    [JsonPropertyName("commission")]
    public OzonMoney? Commission { get; set; }

    [JsonPropertyName("sale_amount")]
    public OzonMoney? SaleAmount { get; set; }

    [JsonPropertyName("coinvestment")]
    public OzonMoney? Coinvestment { get; set; }

    [JsonPropertyName("bonus")]
    public OzonMoney? Bonus { get; set; }
}

internal sealed class OzonAccrualItemFees
{
    [JsonPropertyName("fees")]
    public List<OzonAccrualSkuFees>? Fees { get; set; }
}

internal sealed class OzonAccrualSkuFees
{
    [JsonPropertyName("sku")]
    public long Sku { get; set; }

    [JsonPropertyName("fees")]
    public List<OzonAccrualFee>? Fees { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}

internal sealed class OzonAccrualFee
{
    [JsonPropertyName("type_id")]
    public int TypeId { get; set; }

    [JsonPropertyName("accrued")]
    public OzonMoney? Accrued { get; set; }
}
