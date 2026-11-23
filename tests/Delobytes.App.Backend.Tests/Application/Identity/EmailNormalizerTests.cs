using Delobytes.App.Backend.Identity.Domain;

namespace Delobytes.App.Backend.Tests.Application.Identity;

/// <summary>
/// Tests for EmailNormalizer.
/// </summary>
public class EmailNormalizerTests
{
    [Theory]
    [InlineData("user@example.com", "user@example.com")]
    [InlineData("User@Example.COM", "user@example.com")]
    [InlineData("  user@example.com  ", "user@example.com")]
    [InlineData(" USER@EXAMPLE.COM ", "user@example.com")]
    public void Normalize_VariousCasingAndWhitespace_ReturnsCanonicalValue(string input, string expected)
    {
        Assert.Equal(expected, EmailNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_MissingValue_ReturnsEmptyString(string? input)
    {
        Assert.Equal(string.Empty, EmailNormalizer.Normalize(input));
    }
}
