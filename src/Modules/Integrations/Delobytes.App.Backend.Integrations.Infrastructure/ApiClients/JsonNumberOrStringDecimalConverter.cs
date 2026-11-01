using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;

/// <summary>
/// Reads a decimal member Ozon writes in two different shapes for the same field: a bare JSON number
/// in <c>financial_data.products[]</c> (<c>payout</c>, <c>price</c>, <c>old_price</c>,
/// <c>commission.amount</c>) and a numeric string in <c>products[].price.amount</c>. Writing emits a
/// number, so the model keeps the shape Ozon currently sends.
/// </summary>
internal sealed class JsonNumberOrStringDecimalConverter : JsonConverter<decimal?>
{
    private const NumberStyles AllowedStyles = NumberStyles.Float;

    /// <inheritdoc/>
    public override decimal? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.Number:
                if (reader.TryGetDecimal(out decimal numericValue))
                {
                    return numericValue;
                }

                throw new JsonException(
                    $"Numeric value {Encoding.UTF8.GetString(reader.ValueSpan)} does not fit a decimal.");

            case JsonTokenType.String:
                string? raw = reader.GetString();

                if (string.IsNullOrWhiteSpace(raw))
                {
                    return null;
                }

                if (decimal.TryParse(raw, AllowedStyles, CultureInfo.InvariantCulture, out decimal parsedValue))
                {
                    return parsedValue;
                }

                throw new JsonException($"String value '{raw}' is not a decimal number.");

            default:
                throw new JsonException(
                    $"Expected a number or a numeric string for a decimal member, got {reader.TokenType}.");
        }
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (!value.HasValue)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteNumberValue(value.Value);
    }
}
