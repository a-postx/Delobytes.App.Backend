using System.Text.Json.Serialization;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;

/// <summary>
/// Request body of Ozon's <c>POST /v3/posting/fbo/list</c>.
/// </summary>
/// <remarks>
/// The body is cursor-paginated (v2's <c>offset</c> with its 20000 ceiling is not used), so an
/// empty <see cref="Cursor"/> on the first call is a valid page start rather than a missing filter.
/// </remarks>
public sealed class OzonPostingListRequest
{
    [JsonPropertyName("cursor")]
    public string Cursor { get; init; } = string.Empty;

    [JsonPropertyName("filter")]
    public OzonPostingListFilter Filter { get; init; } = new OzonPostingListFilter();

    [JsonPropertyName("limit")]
    public int Limit { get; init; }

    [JsonPropertyName("sort_dir")]
    public string SortDir { get; init; } = "ASC";

    [JsonPropertyName("translit")]
    public bool Translit { get; init; }

    [JsonPropertyName("with")]
    public OzonPostingListWith With { get; init; } = new OzonPostingListWith();
}

/// <summary>
/// <c>filter</c> section of an Ozon <c>posting/fbo/list</c> request.
/// </summary>
/// <remarks>
/// <see cref="Since"/> and <see cref="To"/> filter on the posting's <c>created_at</c>: FBO exposes
/// no last-change filter, and a period longer than one year is rejected with
/// <c>PERIOD_IS_TOO_LONG</c>.
/// </remarks>
public sealed class OzonPostingListFilter
{
    [JsonPropertyName("since")]
    public string Since { get; init; } = string.Empty;

    [JsonPropertyName("to")]
    public string To { get; init; } = string.Empty;

    [JsonPropertyName("statuses")]
    public List<string> Statuses { get; init; } = new List<string>();
}

/// <summary>
/// <c>with</c> section of an Ozon <c>posting/fbo/list</c> request: which optional blocks of the
/// response to fill.
/// </summary>
public sealed class OzonPostingListWith
{
    [JsonPropertyName("analytics_data")]
    public bool AnalyticsData { get; init; }

    [JsonPropertyName("financial_data")]
    public bool FinancialData { get; init; }

    [JsonPropertyName("legal_info")]
    public bool LegalInfo { get; init; }
}
