using System.Globalization;
using FluentValidation;
using FluentValidation.Results;

namespace Delobytes.App.Backend.Finance.Application.ReadModels;

/// <summary>
/// Keyset cursor of one P&amp;L line page: the ordering value of the last row handed out and its
/// identifier, which breaks ties inside one value. The identifier alone is no longer a cursor —
/// the page is ordered by a value that repeats and, for the margin, is often unknown, so the token
/// carries both the value and the row that produced it.
/// </summary>
public sealed class PnlLineCursor
{
    private const char Separator = '|';
    private const string DateFormat = "yyyy-MM-dd";

    private PnlLineCursor(PnlLineSortKey key, bool descending, string? value, Guid id)
    {
        Key = key;
        Descending = descending;
        Value = value;
        Id = id;
    }

    /// <summary>Gets the ordering key the cursor belongs to.</summary>
    public PnlLineSortKey Key { get; }

    /// <summary>Gets the direction the cursor belongs to.</summary>
    public bool Descending { get; }

    /// <summary>Gets the ordering value of the last row, or null when the key was unknown for it.</summary>
    public string? Value { get; }

    /// <summary>Gets the identifier of the last row, the tiebreak inside one value.</summary>
    public Guid Id { get; }

    /// <summary>Gets the period date of the cursor when it orders by date.</summary>
    public DateOnly? DateValue =>
        Value == null ? null : DateOnly.ParseExact(Value, DateFormat, CultureInfo.InvariantCulture);

    /// <summary>Gets the margin of the cursor when it orders by margin.</summary>
    public decimal? MarginValue =>
        Value == null ? null : decimal.Parse(Value, NumberStyles.Number, CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds the cursor of the last row of a page.
    /// </summary>
    /// <param name="row">Row the token points at.</param>
    /// <param name="sort">Ordering of the page.</param>
    /// <returns>The cursor.</returns>
    public static PnlLineCursor ForRow(PnlLineRow row, PnlLineSort sort)
    {
        string? value = sort.Key switch
        {
            PnlLineSortKey.Margin => row.MarginAmount?.ToString(CultureInfo.InvariantCulture),
            _ => row.PeriodDate?.ToString(DateFormat, CultureInfo.InvariantCulture),
        };

        return new PnlLineCursor(sort.Key, sort.Descending, value, row.Id);
    }

    /// <summary>
    /// Parses the continuation token of a request.
    /// </summary>
    /// <remarks>
    /// A token is only meaningful for the ordering that produced it: the same identifier under
    /// another key would silently skip or repeat rows, so a mismatch is rejected instead of
    /// paging over a different order.
    /// </remarks>
    /// <param name="token">Token of the previous page, or null for the first page.</param>
    /// <param name="sort">Ordering of the current request.</param>
    /// <returns>The cursor, or null when the request asks for the first page.</returns>
    public static PnlLineCursor? Parse(string? token, PnlLineSort sort)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        string[] parts = token.Split(Separator);

        if (parts.Length != 4
            || !Enum.TryParse(parts[0], ignoreCase: true, out PnlLineSortKey key)
            || !Enum.IsDefined(key)
            || !TryParseDirection(parts[1], out bool descending)
            || !Guid.TryParse(parts[3], out Guid id)
            || !IsValueOfKey(key, parts[2]))
        {
            throw new ValidationException(new[]
            {
                new ValidationFailure(
                    "continuationToken",
                    "Токен продолжения имеет неверный формат. Повторите запрос с тем же набором фильтров."),
            });
        }

        if (key != sort.Key || descending != sort.Descending)
        {
            throw new ValidationException(new[]
            {
                new ValidationFailure(
                    "continuationToken",
                    "Токен продолжения относится к другой сортировке: повторите запрос с теми же sortBy и sortDir."),
            });
        }

        return new PnlLineCursor(key, descending, parts[2].Length == 0 ? null : parts[2], id);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return string.Join(Separator, Key.ToString(), Descending ? "desc" : "asc", Value ?? string.Empty, Id);
    }

    private static bool TryParseDirection(string value, out bool descending)
    {
        if (string.Equals(value, "desc", StringComparison.OrdinalIgnoreCase))
        {
            descending = true;
            return true;
        }

        descending = false;

        return string.Equals(value, "asc", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValueOfKey(PnlLineSortKey key, string value)
    {
        if (value.Length == 0)
        {
            return true;
        }

        return key == PnlLineSortKey.Margin
            ? decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
            : DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }
}
