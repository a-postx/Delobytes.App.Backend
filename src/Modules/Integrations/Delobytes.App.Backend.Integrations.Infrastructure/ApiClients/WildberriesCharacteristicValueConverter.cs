using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// Reads Wildberries characteristic values, which are polymorphic on the wire: the same
/// <c>value</c> member is an array of strings for list-type attributes ("Цвет") but a bare
/// number, string or boolean for scalar ones ("Ширина предмета", "Вес товара без упаковки (г)").
/// Writing always emits an array so the channel-specific payload keeps one shape.
/// </summary>
internal sealed class WildberriesCharacteristicValueConverter : JsonConverter<List<string>>
{
    /// <inheritdoc/>
    public override List<string>? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            List<string> arrayValues = new List<string>();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return arrayValues;
                }

                string? element = ReadElementAsString(ref reader);
                if (element != null)
                {
                    arrayValues.Add(element);
                }
            }

            return arrayValues;
        }

        string? scalar = ReadElementAsString(ref reader);

        if (scalar == null)
        {
            return new List<string>();
        }

        return new List<string> { scalar };
    }

    /// <inheritdoc/>
    public override void Write(
        Utf8JsonWriter writer,
        List<string> value,
        JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();

        foreach (string item in value)
        {
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Reads the current token as a string. Returns null for tokens that carry no usable
    /// value, so unexpected shapes (nulls, nested objects) degrade to a skipped element
    /// instead of failing the whole page.
    /// </summary>
    private static string? ReadElementAsString(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                return ReadNumberAsString(ref reader);
            case JsonTokenType.True:
                return bool.TrueString;
            case JsonTokenType.False:
                return bool.FalseString;
            default:
                using (JsonDocument ignored = JsonDocument.ParseValue(ref reader))
                {
                }

                return null;
        }
    }

    /// <summary>
    /// Formats a number invariant-culture so that 0.6 does not become "0,6" on a
    /// comma-decimal locale, and integer-valued decimals stay free of a trailing ".0".
    /// </summary>
    private static string ReadNumberAsString(ref Utf8JsonReader reader)
    {
        if (reader.TryGetInt64(out long integerValue))
        {
            return integerValue.ToString(CultureInfo.InvariantCulture);
        }

        return reader.GetDecimal().ToString(CultureInfo.InvariantCulture);
    }
}
