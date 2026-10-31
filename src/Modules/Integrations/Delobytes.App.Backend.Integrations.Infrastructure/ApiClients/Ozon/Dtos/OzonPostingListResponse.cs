using System.Text.Json.Serialization;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;

/// <summary>
/// Response body of Ozon's <c>POST /v3/posting/fbo/list</c>.
/// </summary>
public sealed class OzonPostingListResponse
{
    [JsonPropertyName("has_next")]
    public bool HasNext { get; init; }

    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("postings")]
    public List<OzonPosting>? Postings { get; init; }
}
