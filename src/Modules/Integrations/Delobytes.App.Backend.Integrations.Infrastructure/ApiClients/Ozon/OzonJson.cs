using System.Text.Json;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;

/// <summary>
/// Serializer settings every Ozon body is written and read with. One instance, so a request body and
/// the response read back from it never disagree on naming or number handling.
/// </summary>
internal static class OzonJson
{
    public static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
}
