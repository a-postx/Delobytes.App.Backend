using System;
using Delobytes.App.Backend.Persistence.Converters;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Persistence;

public class DateTimeOffsetToUtcConvertersTests
{
    private readonly DateTimeOffsetToUtcConverter _converter = new DateTimeOffsetToUtcConverter();

    private readonly NullableDateTimeOffsetToUtcConverter _nullableConverter = new NullableDateTimeOffsetToUtcConverter();

    [Fact]
    public void ConvertToProvider_WhenOffsetIsPositive_NormalizesToUtc()
    {
        // Arrange
        DateTimeOffset source = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(5));

        // Act
        object? converted = _converter.ConvertToProvider(source);

        // Assert
        converted.Should().Be(new DateTimeOffset(2026, 1, 1, 7, 0, 0, TimeSpan.Zero));
        ((DateTimeOffset)converted!).Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ConvertToProvider_WhenOffsetIsNegative_NormalizesToUtc()
    {
        // Arrange
        DateTimeOffset source = new DateTimeOffset(2026, 3, 1, 2, 0, 0, TimeSpan.FromHours(-8));

        // Act
        object? converted = _converter.ConvertToProvider(source);

        // Assert
        converted.Should().Be(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero));
        ((DateTimeOffset)converted!).Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ConvertFromProvider_ReturnsStoredValueUnchanged()
    {
        // Arrange
        DateTimeOffset stored = new DateTimeOffset(2026, 1, 1, 7, 0, 0, TimeSpan.Zero);

        // Act
        object? converted = _converter.ConvertFromProvider(stored);

        // Assert
        converted.Should().Be(stored);
    }

    [Fact]
    public void NullableConverter_ConvertToProvider_NormalizesValueAndPreservesNull()
    {
        // Arrange
        DateTimeOffset source = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(5));

        // Act
        object? converted = _nullableConverter.ConvertToProvider(source);
        object? convertedNull = _nullableConverter.ConvertToProvider(null);

        // Assert
        converted.Should().Be(new DateTimeOffset(2026, 1, 1, 7, 0, 0, TimeSpan.Zero));
        convertedNull.Should().BeNull();
    }

    [Fact]
    public void NullableConverter_ConvertFromProvider_ReturnsStoredValueAndPreservesNull()
    {
        // Arrange
        DateTimeOffset stored = new DateTimeOffset(2026, 1, 1, 7, 0, 0, TimeSpan.Zero);

        // Act
        object? converted = _nullableConverter.ConvertFromProvider(stored);
        object? convertedNull = _nullableConverter.ConvertFromProvider(null);

        // Assert
        converted.Should().Be(stored);
        convertedNull.Should().BeNull();
    }
}
