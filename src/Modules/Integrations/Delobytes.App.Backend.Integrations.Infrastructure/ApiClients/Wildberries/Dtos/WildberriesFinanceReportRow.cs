using System.Text.Json.Serialization;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Wildberries.Dtos;

/// <summary>
/// One row of the Wildberries weekly detailed sales report, restricted to the requested fields.
/// Unknown members are ignored by the serializer.
/// </summary>
internal sealed class WildberriesFinanceReportRow
{
    [JsonPropertyName("reportId")]
    public long ReportId { get; set; }

    [JsonPropertyName("dateFrom")]
    public string? DateFrom { get; set; }

    [JsonPropertyName("dateTo")]
    public string? DateTo { get; set; }

    [JsonPropertyName("createDate")]
    public string? CreateDate { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("rrdId")]
    public long RrdId { get; set; }

    [JsonPropertyName("nmId")]
    public long NmId { get; set; }

    [JsonPropertyName("srid")]
    public string? Srid { get; set; }

    [JsonPropertyName("sellerOperName")]
    public string? SellerOperName { get; set; }

    [JsonPropertyName("saleDt")]
    public string? SaleDate { get; set; }

    [JsonPropertyName("rrDate")]
    public string? RrDate { get; set; }

    [JsonPropertyName("retailAmount")]
    [JsonConverter(typeof(JsonNumberOrStringDecimalConverter))]
    public decimal? RetailAmount { get; set; }

    [JsonPropertyName("acquiringFee")]
    [JsonConverter(typeof(JsonNumberOrStringDecimalConverter))]
    public decimal? AcquiringFee { get; set; }

    [JsonPropertyName("vw")]
    [JsonConverter(typeof(JsonNumberOrStringDecimalConverter))]
    public decimal? Vw { get; set; }

    [JsonPropertyName("vwNds")]
    [JsonConverter(typeof(JsonNumberOrStringDecimalConverter))]
    public decimal? VwNds { get; set; }

    [JsonPropertyName("ppvzReward")]
    [JsonConverter(typeof(JsonNumberOrStringDecimalConverter))]
    public decimal? PpvzReward { get; set; }

    [JsonPropertyName("forPay")]
    [JsonConverter(typeof(JsonNumberOrStringDecimalConverter))]
    public decimal? ForPay { get; set; }

    [JsonPropertyName("deliveryService")]
    [JsonConverter(typeof(JsonNumberOrStringDecimalConverter))]
    public decimal? DeliveryService { get; set; }
}
