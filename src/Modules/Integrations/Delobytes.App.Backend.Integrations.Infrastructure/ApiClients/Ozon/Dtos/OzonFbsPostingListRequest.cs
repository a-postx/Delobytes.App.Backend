using System.Text.Json.Serialization;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;

/// <summary>
/// Request body of the Ozon FBS <c>posting/fbs/list</c> cursor-paginated endpoint.
/// </summary>
public sealed class OzonFbsPostingListRequest
{
    [JsonPropertyName("cursor")]
    public string Cursor { get; init; } = string.Empty;

    [JsonPropertyName("filter")]
    public OzonFbsPostingListFilter Filter { get; init; } = new OzonFbsPostingListFilter();

    [JsonPropertyName("limit")]
    public int Limit { get; init; }

    [JsonPropertyName("sort_dir")]
    public string SortDir { get; init; } = "asc";

    [JsonPropertyName("with")]
    public OzonFbsPostingListWith With { get; init; } = new OzonFbsPostingListWith();
}

/// <summary>
/// The window and statuses <see cref="OzonFbsPostingListRequest"/> asks for. Both bounds are
/// required by the endpoint, so they default to an empty string rather than null: an absent bound
/// is rejected by Ozon instead of being silently widened.
/// </summary>
public sealed class OzonFbsPostingListFilter
{
    [JsonPropertyName("since")]
    public string Since { get; init; } = string.Empty;

    [JsonPropertyName("to")]
    public string To { get; init; } = string.Empty;

    [JsonPropertyName("statuses")]
    public List<string>? Statuses { get; init; }
}

/// <summary>
/// Opt-in sections of an FBS posting page. <c>financial_data</c> is what the mapper reads
/// commission and payout from.
/// </summary>
public sealed class OzonFbsPostingListWith
{
    [JsonPropertyName("financial_data")]
    public bool FinancialData { get; init; }
}
