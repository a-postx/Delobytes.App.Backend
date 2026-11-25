using System.Text.Json;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;

/// <summary>
/// Reads the two posting-list body shapes. Ozon documents the page wrapped in a <c>result</c> object
/// while the FBO endpoint returns it flat, and reading one shape as the other yields an empty page
/// without raising — an import that silently finds no orders.
/// </summary>
internal static class OzonResponseReader
{
    /// <summary>
    /// Reads the flat <c>v3/posting/fbo/list</c> shape, where the page members sit at the top level.
    /// </summary>
    public static OzonPostingListResponse? Flat(string body)
    {
        return JsonSerializer.Deserialize<OzonPostingListResponse>(body, OzonJson.Options);
    }

    /// <summary>
    /// Reads the <c>v4/posting/fbs/list</c> shape. Decided by the presence of <c>result</c> rather
    /// than by trying one shape and falling back, because a missing <c>postings</c> member is
    /// indistinguishable from a genuinely empty page.
    /// </summary>
    public static OzonPostingListResponse? Wrapped(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);

        JsonElement root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("result", out JsonElement result)
            && result.ValueKind == JsonValueKind.Object)
        {
            return result.Deserialize<OzonPostingListResponse>(OzonJson.Options);
        }

        return root.Deserialize<OzonPostingListResponse>(OzonJson.Options);
    }
}
