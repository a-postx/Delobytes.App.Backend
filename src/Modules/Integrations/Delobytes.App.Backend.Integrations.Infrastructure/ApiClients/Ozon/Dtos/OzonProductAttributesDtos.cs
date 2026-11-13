using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;

/// <summary>
/// Request body of <c>POST /v4/product/info/attributes</c>, which both lists the seller's cards and
/// returns their full attributes.
/// </summary>
public sealed class OzonProductAttributesRequest
{
    [JsonPropertyName("filter")]
    public OzonProductAttributesFilter Filter { get; init; } = new();

    /// <summary>
    /// Cursor of the next page. Omitted entirely on the first request: an empty string is a value
    /// and is serialized, which the API reads as a cursor rather than as "the beginning".
    /// </summary>
    [JsonPropertyName("last_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? LastId { get; init; }

    [JsonPropertyName("limit")]
    public int Limit { get; init; }

    /// <summary>
    /// Kept identical on every request so the paging window stays ordered consistently.
    /// </summary>
    [JsonPropertyName("sort_dir")]
    public string SortDir { get; init; } = "ASC";
}

public sealed class OzonProductAttributesFilter
{
    /// <summary>
    /// Only on-sale cards. Governs the whole import: archived, hidden and removed cards are not
    /// imported.
    /// </summary>
    [JsonPropertyName("visibility")]
    public string Visibility { get; init; } = "VISIBLE";
}

public sealed class OzonProductAttributesResponse
{
    [JsonPropertyName("result")]
    public List<OzonProductAttributesItem> Result { get; init; } = new();

    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("last_id")]
    public string? LastId { get; init; }
}

/// <summary>
/// One card of the attributes response. Only the members the mapper reads are declared; the rest of
/// the payload, including the rich-content attributes, is ignored.
/// </summary>
public sealed class OzonProductAttributesItem
{
    /// <summary>
    /// Ozon's internal identifier. Read for log messages only and never mapped to the card.
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }

    /// <summary>
    /// The marketplace article shown to the seller. This is the external product id of the card.
    /// </summary>
    [JsonPropertyName("sku")]
    public long Sku { get; init; }

    [JsonPropertyName("offer_id")]
    public string? OfferId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("barcode")]
    public string? Barcode { get; init; }

    [JsonPropertyName("barcodes")]
    public List<string> Barcodes { get; init; } = new();

    /// <summary>Package depth reported by the marketplace, in <see cref="DimensionUnit"/>.</summary>
    [JsonPropertyName("depth")]
    public decimal Depth { get; init; }

    [JsonPropertyName("width")]
    public decimal Width { get; init; }

    [JsonPropertyName("height")]
    public decimal Height { get; init; }

    [JsonPropertyName("dimension_unit")]
    public string? DimensionUnit { get; init; }

    [JsonPropertyName("weight")]
    public decimal Weight { get; init; }

    [JsonPropertyName("weight_unit")]
    public string? WeightUnit { get; init; }

    [JsonPropertyName("primary_image")]
    public string? PrimaryImage { get; init; }

    [JsonPropertyName("images")]
    public List<string> Images { get; init; } = new();

    [JsonPropertyName("attributes")]
    public List<OzonProductAttribute> Attributes { get; init; } = new();
}

public sealed class OzonProductAttribute
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("complex_id")]
    public long ComplexId { get; init; }

    [JsonPropertyName("values")]
    public List<OzonProductAttributeValue> Values { get; init; } = new();
}

public sealed class OzonProductAttributeValue
{
    [JsonPropertyName("dictionary_value_id")]
    public long DictionaryValueId { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }
}
