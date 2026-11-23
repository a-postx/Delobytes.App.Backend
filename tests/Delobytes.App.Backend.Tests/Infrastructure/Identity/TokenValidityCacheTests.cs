using Delobytes.App.Backend.Identity.Infrastructure.Services;
using FluentAssertions;

namespace Delobytes.App.Backend.Tests.Infrastructure.Identity;

/// <summary>
/// Tests for the in-memory revocation cache used during JWT validation.
/// </summary>
public class TokenValidityCacheTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private readonly MutableTimeProvider _timeProvider = new MutableTimeProvider { UtcNow = Now };
    private readonly TokenValidityCache _cache;

    public TokenValidityCacheTests()
    {
        _cache = new TokenValidityCache(_timeProvider);
    }

    [Fact]
    public void TryGet_AfterSet_ReturnsCachedValue()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        DateTimeOffset validAfter = Now.AddMinutes(-5);

        // Act
        _cache.Set(userId, validAfter);

        // Assert
        _cache.TryGet(userId, out DateTimeOffset? cached).Should().BeTrue();
        cached.Should().Be(validAfter);
    }

    [Fact]
    public void TryGet_AfterLifetime_ReturnsFalse()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        _cache.Set(userId, Now);

        // Act
        _timeProvider.UtcNow = Now.AddMinutes(2);

        // Assert
        _cache.TryGet(userId, out _).Should().BeFalse();
    }

    [Fact]
    public void TryGet_AfterInvalidate_ReturnsFalse()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        _cache.Set(userId, Now);

        // Act
        _cache.Invalidate(userId);

        // Assert
        _cache.TryGet(userId, out _).Should().BeFalse();
    }

    [Fact]
    public void TryGet_WithNullRevocationInstant_ReturnsTrueWithNull()
    {
        // Arrange
        Guid userId = Guid.NewGuid();

        // Act
        _cache.Set(userId, null);

        // Assert
        _cache.TryGet(userId, out DateTimeOffset? cached).Should().BeTrue();
        cached.Should().BeNull();
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
