using System.Text.Json.Serialization;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// Request model for Wildberries POST /content/v2/get/cards/list endpoint.
/// </summary>
internal sealed class WildberriesGetCardsRequest
{
    [JsonPropertyName("settings")]
    public WildberriesCardSettings Settings { get; set; } = default!;

    [JsonPropertyName("filter")]
    public WildberriesCardFilter Filter { get; set; } = new();
}

/// <summary>
/// Settings for retrieving product cards, including cursor for pagination.
/// </summary>
internal sealed class WildberriesCardSettings
{
    [JsonPropertyName("cursor")]
    public WildberriesCursorRequest Cursor { get; set; } = default!;

    [JsonPropertyName("filter")]
    public WildberriesCardFilter Filter { get; set; } = new();
}

/// <summary>
/// Cursor parameters for paginated card retrieval.
/// </summary>
internal sealed class WildberriesCursorRequest
{
    /// <summary>
    /// Maximum number of cards to retrieve (1-100).
    /// </summary>
    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    /// <summary>
    /// UTC timestamp for cursor-based pagination. Optional for first page.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UpdatedAt { get; set; }

    /// <summary>
    /// Wildberries internal product ID (nmID) for cursor-based pagination. Optional for first page.
    /// </summary>
    [JsonPropertyName("nmID")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? NmId { get; set; }
}

/// <summary>
/// Filter parameters for card retrieval. Empty for retrieving all cards.
/// </summary>
internal sealed class WildberriesCardFilter
{
    [JsonPropertyName("textSearch")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TextSearch { get; set; }

    [JsonPropertyName("withPhoto")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WithPhoto { get; set; }
}

/// <summary>
/// Response model from Wildberries POST /content/v2/get/cards/list endpoint.
/// </summary>
internal sealed class WildberriesGetCardsResponse
{
    [JsonPropertyName("cards")]
    public List<WildberriesCardDto> Cards { get; set; } = new();

    [JsonPropertyName("cursor")]
    public WildberriesCursorResponse? Cursor { get; set; }
}

/// <summary>
/// Cursor metadata returned in the response for pagination.
/// </summary>
internal sealed class WildberriesCursorResponse
{
    /// <summary>
    /// UTC timestamp of the last card in the current page.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    /// <summary>
    /// Wildberries internal product ID (nmID) of the last card in the current page.
    /// </summary>
    [JsonPropertyName("nmID")]
    public long? NmId { get; set; }

    /// <summary>
    /// Total number of cards matching the filter.
    /// </summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }
}

/// <summary>
/// Product card data transfer object from Wildberries API.
/// Internal representation that will be mapped to WildberriesCardSnapshot.
/// </summary>
internal sealed class WildberriesCardDto
{
    [JsonPropertyName("nmID")]
    public long NmId { get; set; }

    [JsonPropertyName("imtID")]
    public long? ImtId { get; set; }

    [JsonPropertyName("nmUUID")]
    public string? NmUuid { get; set; }

    [JsonPropertyName("subjectID")]
    public int? SubjectId { get; set; }

    [JsonPropertyName("subjectName")]
    public string? SubjectName { get; set; }

    [JsonPropertyName("vendorCode")]
    public string VendorCode { get; set; } = default!;

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("dimensions")]
    public WildberriesDimensions? Dimensions { get; set; }

    [JsonPropertyName("characteristics")]
    public List<WildberriesCharacteristic>? Characteristics { get; set; }

    [JsonPropertyName("sizes")]
    public List<WildberriesSize>? Sizes { get; set; }

    [JsonPropertyName("tags")]
    public List<WildberriesTag>? Tags { get; set; }

    [JsonPropertyName("photos")]
    public List<WildberriesPhoto>? Photos { get; set; }

    [JsonPropertyName("video")]
    public string? Video { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }
}

/// <summary>
/// Product dimensions from Wildberries. Length/width/height are in cm,
/// weightBrutto is the packed gross weight in kg (up to 3 decimal places).
/// </summary>
internal sealed class WildberriesDimensions
{
    [JsonPropertyName("length")]
    public int? Length { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("weightBrutto")]
    public decimal? WeightBrutto { get; set; }
}

/// <summary>
/// Product characteristic (attribute) from Wildberries.
/// </summary>
internal sealed class WildberriesCharacteristic
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Wildberries sends a string array for list attributes but a bare number, string or
    /// boolean for scalar ones, so the default collection converter cannot read this member.
    /// </summary>
    [JsonPropertyName("value")]
    [JsonConverter(typeof(WildberriesCharacteristicValueConverter))]
    public List<string>? Value { get; set; }
}

/// <summary>
/// Size/SKU information from Wildberries.
/// </summary>
internal sealed class WildberriesSize
{
    [JsonPropertyName("chrtID")]
    public long? ChrtId { get; set; }

    [JsonPropertyName("techSize")]
    public string? TechSize { get; set; }

    [JsonPropertyName("wbSize")]
    public string? WbSize { get; set; }

    [JsonPropertyName("skus")]
    public List<string>? Skus { get; set; }
}

/// <summary>
/// Tag metadata from Wildberries.
/// </summary>
internal sealed class WildberriesTag
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }
}

/// <summary>
/// Photo metadata from Wildberries.
/// </summary>
internal sealed class WildberriesPhoto
{
    [JsonPropertyName("big")]
    public string? Big { get; set; }

    [JsonPropertyName("c246x328")]
    public string? C246x328 { get; set; }

    [JsonPropertyName("c516x688")]
    public string? C516x688 { get; set; }

    [JsonPropertyName("square")]
    public string? Square { get; set; }

    [JsonPropertyName("tm")]
    public string? Tm { get; set; }
}
