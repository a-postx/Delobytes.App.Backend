using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Delobytes.App.Backend.Persistence.Converters;

/// <summary>
/// Normalises <see cref="DateTimeOffset"/> to UTC on the way to the provider.
/// PostgreSQL's <c>timestamp with time zone</c> accepts only offset 0, so a value carrying a
/// channel-local offset (Marketplace APIs report wall-clock time) otherwise fails the write
/// instead of being stored as the instant it denotes.
/// </summary>
public sealed class DateTimeOffsetToUtcConverter : ValueConverter<DateTimeOffset, DateTimeOffset>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DateTimeOffsetToUtcConverter"/> class.
    /// </summary>
    public DateTimeOffsetToUtcConverter()
        : base(v => v.ToUniversalTime(), v => v)
    {
    }
}

/// <summary>
/// Nullable counterpart of <see cref="DateTimeOffsetToUtcConverter"/>, for the properties a
/// module models as <see cref="Nullable{DateTimeOffset}"/>.
/// </summary>
public sealed class NullableDateTimeOffsetToUtcConverter : ValueConverter<DateTimeOffset?, DateTimeOffset?>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NullableDateTimeOffsetToUtcConverter"/> class.
    /// </summary>
    public NullableDateTimeOffsetToUtcConverter()
        : base(v => v.HasValue ? v.Value.ToUniversalTime() : v, v => v)
    {
    }
}
