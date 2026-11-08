using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Reads both the current payload (with an explicit <see cref="OrdersImportCheckpointPhase"/>) and
/// the legacy shape that carried only <c>IsFetchCompleted</c>. Payload JSON is data, not schema:
/// rows written before the phase existed must stay readable without a migration.
/// </summary>
public sealed class OrdersImportCheckpointPayloadConverter : JsonConverter<OrdersImportCheckpointPayload>
{
    private const string PhaseProperty = "Phase";
    private const string LegacyIsFetchCompletedProperty = "IsFetchCompleted";
    private const string TotalBatchesProperty = "TotalBatches";
    private const string WindowToProperty = "WindowTo";
    private const string IsBackfillProperty = "IsBackfill";
    private const string IsBackfillCompletedProperty = "IsBackfillCompleted";
    private const string HandoverWatermarkProperty = "HandoverWatermark";
    private const string MaxChangedAtProperty = "MaxChangedAt";
    private const string PagesFetchedProperty = "PagesFetched";

    public override OrdersImportCheckpointPayload Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);

        JsonElement root = document.RootElement;

        return new OrdersImportCheckpointPayload
        {
            Phase = ReadPhase(root),
            TotalBatches = ReadInt32(root, TotalBatchesProperty),
            WindowTo = ReadDateTimeOffset(root, WindowToProperty),
            IsBackfill = ReadBoolean(root, IsBackfillProperty),
            IsBackfillCompleted = ReadBoolean(root, IsBackfillCompletedProperty),
            HandoverWatermark = ReadNullableDateTimeOffset(root, HandoverWatermarkProperty),
            MaxChangedAt = ReadNullableDateTimeOffset(root, MaxChangedAtProperty),
            PagesFetched = ReadInt32(root, PagesFetchedProperty),
        };
    }

    public override void Write(
        Utf8JsonWriter writer, OrdersImportCheckpointPayload value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(PhaseProperty, (int)value.Phase);
        writer.WriteNumber(TotalBatchesProperty, value.TotalBatches);
        writer.WriteString(WindowToProperty, value.WindowTo);
        writer.WriteBoolean(IsBackfillProperty, value.IsBackfill);
        writer.WriteBoolean(IsBackfillCompletedProperty, value.IsBackfillCompleted);
        WriteNullableDateTimeOffset(writer, HandoverWatermarkProperty, value.HandoverWatermark);
        WriteNullableDateTimeOffset(writer, MaxChangedAtProperty, value.MaxChangedAt);
        writer.WriteNumber(PagesFetchedProperty, value.PagesFetched);
        writer.WriteEndObject();
    }

    private static OrdersImportCheckpointPhase ReadPhase(JsonElement root)
    {
        int phaseValue;

        if (TryGetProperty(root, PhaseProperty, out JsonElement phaseElement)
            && phaseElement.ValueKind == JsonValueKind.Number
            && phaseElement.TryGetInt32(out phaseValue))
        {
            return (OrdersImportCheckpointPhase)phaseValue;
        }

        // Rows written before the phase existed carried only IsFetchCompleted: true meant the
        // fetch had finished, false meant progress only, and an absent flag is progress too.
        if (TryGetProperty(root, LegacyIsFetchCompletedProperty, out JsonElement legacy)
            && legacy.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return legacy.GetBoolean()
                ? OrdersImportCheckpointPhase.FetchCompleted
                : OrdersImportCheckpointPhase.InProgress;
        }

        return OrdersImportCheckpointPhase.InProgress;
    }

    private static int ReadInt32(JsonElement root, string propertyName)
    {
        int value;

        if (TryGetProperty(root, propertyName, out JsonElement element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value))
        {
            return value;
        }

        return 0;
    }

    private static bool ReadBoolean(JsonElement root, string propertyName)
    {
        if (TryGetProperty(root, propertyName, out JsonElement element)
            && element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return element.GetBoolean();
        }

        return false;
    }

    private static DateTimeOffset ReadDateTimeOffset(JsonElement root, string propertyName)
    {
        return ReadNullableDateTimeOffset(root, propertyName) ?? default;
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out JsonElement element)
            || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? raw = element.GetString();

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset value)
            ? value
            : null;
    }

    private static void WriteNullableDateTimeOffset(
        Utf8JsonWriter writer, string propertyName, DateTimeOffset? value)
    {
        if (value.HasValue)
        {
            writer.WriteString(propertyName, value.Value);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
